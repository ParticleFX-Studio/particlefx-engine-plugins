// ParticleFX Studio for Unity. MIT licensed.
// Turns manifest frames into Unity sprite rects. Pure C#, no UnityEngine, so
// the rect and pivot maths is unit tested outside Unity.
//
// Conventions:
//   manifest: top-left origin, y down; pivot 0..1 of the FULL frame from the top-left.
//   Unity:    sprite rects are in texture pixels from the bottom-left, y up;
//             a sprite pivot is 0..1 of ITS OWN rect from the bottom-left.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ParticleFX.Studio.Editor.Core
{
    /// <summary>A sprite to create: Unity rect (bottom-left origin) plus a normalised pivot.</summary>
    public struct SpriteDef
    {
        public string Name;
        public int X, Y, W, H;
        public double PivotX, PivotY;
    }

    public sealed class SpriteSheetLayout
    {
        /// <summary>One sprite per unique frame rect (and offset), in first-use order.</summary>
        public readonly List<SpriteDef> Sprites = new List<SpriteDef>();
        /// <summary>For each animation frame in play order, the index into Sprites.</summary>
        public readonly List<int> FrameToSprite = new List<int>();

        public int TextureWidth { get; private set; }
        public int TextureHeight { get; private set; }

        /// <summary>
        /// Builds the layout. textureWidth/Height are the real PNG size (read from
        /// its header); rects outside it are rejected.
        /// </summary>
        public static SpriteSheetLayout Build(EffectManifest manifest, int textureWidth, int textureHeight)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            if (textureWidth <= 0 || textureHeight <= 0) throw new PfxBundleException("The sprite sheet has no size.");

            var layout = new SpriteSheetLayout { TextureWidth = textureWidth, TextureHeight = textureHeight };
            var keyed = new Dictionary<string, int>(StringComparer.Ordinal);
            string prefix = string.IsNullOrEmpty(manifest.Base) ? "frame" : manifest.Base;

            for (int i = 0; i < manifest.Frames.Count; i++)
            {
                var f = manifest.Frames[i];
                if (f.X + f.W > textureWidth || f.Y + f.H > textureHeight)
                {
                    throw new PfxBundleException(string.Format(CultureInfo.InvariantCulture,
                        "Frame {0} ({1},{2} {3}x{4}) lies outside the {5}x{6} sprite sheet.",
                        i, f.X, f.Y, f.W, f.H, textureWidth, textureHeight));
                }

                // Frames may repeat a rect (a held frame, or a deduped packed rect).
                // Same rect and same offset means the same sprite. The same rect with
                // a different offset needs its own sprite, as its pivot differs.
                string key = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5}",
                    f.X, f.Y, f.W, f.H, f.OffsetX, f.OffsetY);
                int index;
                if (!keyed.TryGetValue(key, out index))
                {
                    index = layout.Sprites.Count;
                    keyed[key] = index;
                    var def = new SpriteDef
                    {
                        Name = prefix + "_" + index.ToString(CultureInfo.InvariantCulture),
                        X = f.X,
                        Y = FlipY(f.Y, f.H, textureHeight),
                        W = f.W,
                        H = f.H,
                    };
                    double px, py;
                    PivotForFrame(manifest, f, out px, out py);
                    def.PivotX = px;
                    def.PivotY = py;
                    layout.Sprites.Add(def);
                }
                layout.FrameToSprite.Add(index);
            }
            return layout;
        }

        /// <summary>Converts a top-left y (y down) rect origin to Unity's bottom-left y.</summary>
        public static int FlipY(int topY, int height, int textureHeight)
        {
            return textureHeight - topY - height;
        }

        /// <summary>
        /// The effect pivot expressed in a frame rect's own normalised space,
        /// bottom-left origin. For a trimmed rect the pivot is moved by the
        /// rect's offset inside the full frame so all frames line up; the result
        /// can fall outside 0..1 when the trimmed rect does not cover the pivot.
        /// </summary>
        public static void PivotForFrame(EffectManifest m, ManifestFrame f, out double pivotX, out double pivotY)
        {
            // Pivot in full-frame pixels, top-left origin, y down.
            double fullX = m.PivotX * m.FrameWidth;
            double fullY = m.PivotY * m.FrameHeight;
            // Relative to the rect's top-left inside the full frame.
            double localX = fullX - f.OffsetX;
            double localYDown = fullY - f.OffsetY;
            pivotX = localX / f.W;
            pivotY = 1.0 - localYDown / f.H;
        }

        /// <summary>The clip length in seconds: frame count / fps.</summary>
        public static double ClipLength(EffectManifest m)
        {
            return m.Frames.Count / m.Fps;
        }

        /// <summary>Key time of animation frame i.</summary>
        public static double FrameTime(int frameIndex, double fps)
        {
            return frameIndex / fps;
        }

        /// <summary>Smallest power-of-two max texture size (Unity's allowed values) that keeps the sheet unscaled.</summary>
        public static int MaxTextureSizeFor(int width, int height)
        {
            int needed = Math.Max(width, height);
            int size = 2048; // Unity's default; never go below it
            while (size < needed && size < 16384) size *= 2;
            return size;
        }
    }
}
