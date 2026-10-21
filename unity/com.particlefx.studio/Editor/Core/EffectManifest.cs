// ParticleFX Studio for Unity. MIT licensed.
// particlefx.json model (bundle format 1). Pure C#, no UnityEngine.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ParticleFX.Studio.Editor.Core
{
    /// <summary>Thrown when a bundle or manifest is unusable. The message is shown to the user.</summary>
    public sealed class PfxBundleException : Exception
    {
        public PfxBundleException(string message) : base(message) { }
    }

    /// <summary>One animation frame: a rect in the sheet (top-left origin, y down).</summary>
    public struct ManifestFrame
    {
        public int X, Y, W, H;
        /// <summary>Where a trimmed rect sits inside the full frame (top-left, y down). 0 for grid.</summary>
        public int OffsetX, OffsetY;
    }

    public sealed class EffectManifest
    {
        public const int SupportedFormat = 1;

        public int Format;
        public string Engine;
        public string Id;
        public string Slug;
        public string Title;
        public string Base;
        public string Image;
        public string NormalMap;
        public int FrameWidth;
        public int FrameHeight;
        public int Columns;
        public int Rows;
        public List<ManifestFrame> Frames = new List<ManifestFrame>();
        public double Fps;
        public bool Loop;
        public double Duration;
        /// <summary>Effect centre in the full frame, 0..1 from the top-left, y down.</summary>
        public double PivotX = 0.5, PivotY = 0.5;
        public string Blend = "normal";
        public bool PixelArt;
        public double PixelsPerUnit = 100;
        public string SourceUrl;
        public string License;

        /// <summary>Non fatal problems found while parsing (shown as warnings).</summary>
        public List<string> Warnings = new List<string>();

        public bool IsAdditive { get { return string.Equals(Blend, "add", StringComparison.OrdinalIgnoreCase); } }

        public static EffectManifest Parse(string json)
        {
            Dictionary<string, object> root;
            try { root = PfxJson.ParseObject(json); }
            catch (PfxJsonException e) { throw new PfxBundleException("particlefx.json is not valid JSON: " + e.Message); }

            var m = new EffectManifest();
            m.Format = PfxJson.GetInt(root, "format", 0);
            m.Engine = PfxJson.GetString(root, "engine", "");
            m.Id = PfxJson.GetString(root, "id", "");
            m.Slug = PfxJson.GetString(root, "slug", "");
            m.Title = PfxJson.GetString(root, "title", "");
            m.Base = PfxJson.GetString(root, "base", "");
            m.Image = PfxJson.GetString(root, "image", "");
            m.NormalMap = PfxJson.GetString(root, "normal_map", null);
            if (string.IsNullOrEmpty(m.NormalMap)) m.NormalMap = null;
            m.FrameWidth = PfxJson.GetInt(root, "frame_width", 0);
            m.FrameHeight = PfxJson.GetInt(root, "frame_height", 0);
            m.Columns = PfxJson.GetInt(root, "columns", 0);
            m.Rows = PfxJson.GetInt(root, "rows", 0);
            m.Fps = PfxJson.GetDouble(root, "fps", 0);
            m.Loop = PfxJson.GetBool(root, "loop", false);
            m.Duration = PfxJson.GetDouble(root, "duration", 0);
            m.Blend = PfxJson.GetString(root, "blend", "normal");
            m.PixelArt = PfxJson.GetBool(root, "pixel_art", false);
            m.PixelsPerUnit = PfxJson.GetDouble(root, "pixels_per_unit", 100);
            m.SourceUrl = PfxJson.GetString(root, "source_url", null);
            m.License = PfxJson.GetString(root, "license", null);

            var pivot = PfxJson.GetObject(root, "pivot");
            if (pivot != null)
            {
                m.PivotX = PfxJson.GetDouble(pivot, "x", 0.5);
                m.PivotY = PfxJson.GetDouble(pivot, "y", 0.5);
            }
            else
            {
                m.Warnings.Add("The manifest has no pivot; using the frame centre.");
            }

            var frames = PfxJson.GetArray(root, "frames");
            if (frames != null)
            {
                foreach (var f in frames)
                {
                    var fo = f as Dictionary<string, object>;
                    if (fo == null) throw new PfxBundleException("particlefx.json has a frame that is not an object.");
                    m.Frames.Add(new ManifestFrame
                    {
                        X = PfxJson.GetInt(fo, "x", 0),
                        Y = PfxJson.GetInt(fo, "y", 0),
                        W = PfxJson.GetInt(fo, "w", 0),
                        H = PfxJson.GetInt(fo, "h", 0),
                        OffsetX = PfxJson.GetInt(fo, "offset_x", 0),
                        OffsetY = PfxJson.GetInt(fo, "offset_y", 0),
                    });
                }
            }

            m.Validate();
            return m;
        }

        void Validate()
        {
            if (Format <= 0) throw new PfxBundleException("particlefx.json has no format number. Is this a ParticleFX Studio bundle?");
            if (Format > SupportedFormat)
                Warnings.Add("This bundle uses format " + Format + "; this package knows format " + SupportedFormat
                    + ". Update the ParticleFX Studio package if the import looks wrong.");
            if (string.IsNullOrEmpty(Image)) throw new PfxBundleException("particlefx.json does not name the sprite sheet image.");
            if (FrameWidth <= 0 || FrameHeight <= 0) throw new PfxBundleException("particlefx.json has no frame size.");
            if (Frames.Count == 0) throw new PfxBundleException("particlefx.json has no frames.");
            for (int i = 0; i < Frames.Count; i++)
            {
                var f = Frames[i];
                if (f.W <= 0 || f.H <= 0 || f.X < 0 || f.Y < 0)
                    throw new PfxBundleException("Frame " + i + " has an invalid rect.");
            }
            if (!(Fps > 0) || Fps > 240)
            {
                Warnings.Add("The manifest fps is missing or out of range; using 30.");
                Fps = 30;
            }
            if (!(PixelsPerUnit > 0))
            {
                Warnings.Add("The manifest pixels_per_unit is missing; using 100.");
                PixelsPerUnit = 100;
            }
            if (double.IsNaN(PivotX) || double.IsInfinity(PivotX)) PivotX = 0.5;
            if (double.IsNaN(PivotY) || double.IsInfinity(PivotY)) PivotY = 0.5;

            if (string.IsNullOrEmpty(Base)) Base = StripExtension(Image);
            if (string.IsNullOrEmpty(Slug)) Slug = Base;
            if (string.IsNullOrEmpty(Title)) Title = Slug;
        }

        static string StripExtension(string file)
        {
            int slash = Math.Max(file.LastIndexOf('/'), file.LastIndexOf('\\'));
            var name = slash >= 0 ? file.Substring(slash + 1) : file;
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} ({1} frames at {2} fps, {3})",
                Title, Frames.Count, Fps, Loop ? "loop" : "one-shot");
        }
    }
}
