// ParticleFX Studio for Unity. MIT licensed.
// Reads a ParticleFX zip bundle into memory and validates it. Pure C#.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ParticleFX.Studio.Editor.Core
{
    public sealed class EffectBundle
    {
        public const string ManifestName = "particlefx.json";
        /// <summary>Refuse entries larger than this when unpacked (a sheet is a few MB at most).</summary>
        const long MaxEntryBytes = 256L * 1024 * 1024;

        public EffectManifest Manifest;
        public string ManifestJson;
        /// <summary>File name (no folders) to bytes, for every file in the zip.</summary>
        public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        public byte[] Image;
        public byte[] NormalMap;
        public int ImageWidth, ImageHeight;
        public SpriteSheetLayout Layout;

        public static EffectBundle FromZip(byte[] zipBytes)
        {
            if (zipBytes == null || zipBytes.Length < 4) throw new PfxBundleException("The download is empty.");
            if (!(zipBytes[0] == 'P' && zipBytes[1] == 'K'))
                throw new PfxBundleException("The file is not a zip archive.");
            using (var ms = new MemoryStream(zipBytes, false))
                return FromZip(ms);
        }

        public static EffectBundle FromZip(Stream zipStream)
        {
            var bundle = new EffectBundle();
            try
            {
                using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, true))
                {
                    foreach (var entry in zip.Entries)
                    {
                        // Directories have an empty Name. Only the file name is used, so a
                        // crafted path ("../x") can never escape the target folder.
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        if (entry.Length > MaxEntryBytes) throw new PfxBundleException("A file in the bundle is too large: " + entry.Name);
                        var name = SafeFileName(entry.Name);
                        if (name == null) continue;
                        using (var s = entry.Open())
                        using (var copy = new MemoryStream())
                        {
                            s.CopyTo(copy);
                            bundle.Files[name] = copy.ToArray();
                        }
                    }
                }
            }
            catch (InvalidDataException e)
            {
                throw new PfxBundleException("The zip archive is damaged: " + e.Message);
            }

            byte[] manifestBytes;
            if (!bundle.Files.TryGetValue(ManifestName, out manifestBytes))
                throw new PfxBundleException("The zip has no particlefx.json. Is it a ParticleFX Studio Unity or Godot bundle?");
            bundle.ManifestJson = DecodeUtf8(manifestBytes);
            bundle.Manifest = EffectManifest.Parse(bundle.ManifestJson);

            var imageName = SafeFileName(bundle.Manifest.Image);
            if (imageName == null || !bundle.Files.TryGetValue(imageName, out bundle.Image))
                throw new PfxBundleException("The zip does not contain the sprite sheet " + bundle.Manifest.Image + ".");
            bundle.Manifest.Image = imageName;

            int w, h;
            if (!PngInfo.TryReadSize(bundle.Image, out w, out h))
                throw new PfxBundleException(imageName + " is not a PNG image.");
            bundle.ImageWidth = w;
            bundle.ImageHeight = h;

            if (bundle.Manifest.NormalMap != null)
            {
                var nName = SafeFileName(bundle.Manifest.NormalMap);
                if (nName != null && bundle.Files.TryGetValue(nName, out bundle.NormalMap))
                {
                    bundle.Manifest.NormalMap = nName;
                    int nw, nh;
                    if (!PngInfo.TryReadSize(bundle.NormalMap, out nw, out nh) || nw != w || nh != h)
                    {
                        bundle.Manifest.Warnings.Add("The normal map size does not match the sprite sheet; it was skipped.");
                        bundle.NormalMap = null;
                        bundle.Manifest.NormalMap = null;
                    }
                }
                else
                {
                    bundle.Manifest.Warnings.Add("The manifest names a normal map that is not in the zip; it was skipped.");
                    bundle.Manifest.NormalMap = null;
                }
            }

            bundle.Layout = SpriteSheetLayout.Build(bundle.Manifest, w, h);
            return bundle;
        }

        static string DecodeUtf8(byte[] bytes)
        {
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        }

        /// <summary>The last path segment if it is a plain, safe file name; else null.</summary>
        public static string SafeFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            var name = slash >= 0 ? path.Substring(slash + 1) : path;
            if (name.Length == 0 || name == "." || name == "..") return null;
            foreach (var c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.' || c == ' ';
                if (!ok) return null;
            }
            if (name.StartsWith(".", StringComparison.Ordinal)) return null;
            return name;
        }
    }

    public static class PngInfo
    {
        static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>Reads width and height from the IHDR chunk.</summary>
        public static bool TryReadSize(byte[] png, out int width, out int height)
        {
            width = height = 0;
            if (png == null || png.Length < 24) return false;
            for (int i = 0; i < Signature.Length; i++) if (png[i] != Signature[i]) return false;
            if (png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R') return false;
            width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            return width > 0 && height > 0;
        }
    }
}
