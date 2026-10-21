// ParticleFX Studio for Unity. MIT licensed.
using ParticleFX.Studio.Editor.Core;
using UnityEditor;

namespace ParticleFX.Studio.Editor
{
    /// <summary>
    /// Per-user settings in EditorPrefs (stored per machine, never inside the
    /// project, so the API key is not committed to version control).
    /// </summary>
    internal static class PfxSettings
    {
        public const string PackageVersion = "0.1.0";

        const string KeyApiKey = "ParticleFXStudio.ApiKey";
        const string KeyApiBase = "ParticleFXStudio.ApiBase";
        const string KeyTarget = "ParticleFXStudio.TargetFolder";
        const string KeyScale = "ParticleFXStudio.Scale";
        const string KeyNormal = "ParticleFXStudio.NormalMap";
        const string KeyPacked = "ParticleFXStudio.PackedLayout";

        public static string ApiKey
        {
            get { return EditorPrefs.GetString(KeyApiKey, ""); }
            set
            {
                if (string.IsNullOrEmpty(value)) EditorPrefs.DeleteKey(KeyApiKey);
                else EditorPrefs.SetString(KeyApiKey, value.Trim());
            }
        }

        public static bool HasApiKey { get { return !string.IsNullOrEmpty(ApiKey); } }

        /// <summary>API base URL; override for development (EditorPrefs key ParticleFXStudio.ApiBase).</summary>
        public static string ApiBase
        {
            get { return PfxApiRoutes.NormalizeBase(EditorPrefs.GetString(KeyApiBase, PfxApiRoutes.DefaultApiBase)); }
            set
            {
                var v = PfxApiRoutes.NormalizeBase(value);
                if (v == PfxApiRoutes.DefaultApiBase) EditorPrefs.DeleteKey(KeyApiBase);
                else EditorPrefs.SetString(KeyApiBase, v);
            }
        }

        public static string TargetFolder
        {
            get { return PfxPaths.NormalizeAssetFolder(EditorPrefs.GetString(KeyTarget, PfxPaths.DefaultTargetFolder)) ?? PfxPaths.DefaultTargetFolder; }
            set { EditorPrefs.SetString(KeyTarget, PfxPaths.NormalizeAssetFolder(value) ?? PfxPaths.DefaultTargetFolder); }
        }

        public static int Scale
        {
            get { return EditorPrefs.GetInt(KeyScale, 1); }
            set { EditorPrefs.SetInt(KeyScale, value < 1 ? 1 : value); }
        }

        public static bool NormalMap
        {
            get { return EditorPrefs.GetBool(KeyNormal, false); }
            set { EditorPrefs.SetBool(KeyNormal, value); }
        }

        public static bool PackedLayout
        {
            get { return EditorPrefs.GetBool(KeyPacked, false); }
            set { EditorPrefs.SetBool(KeyPacked, value); }
        }
    }
}
