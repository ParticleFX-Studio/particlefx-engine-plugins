// ParticleFX Studio for Unity. MIT licensed.
// Plugin API request building and response parsing (docs/ENGINE_PLUGINS.md).
// Pure C#, no UnityEngine.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ParticleFX.Studio.Editor.Core
{
    public sealed class PfxAccount
    {
        public string Plan = "free";
        public bool HasPlan;
        /// <summary>null when uncapped.</summary>
        public int? ExportsLeftToday;
        /// <summary>null when uncapped.</summary>
        public int? ExportsPerDay;
        public string PricingUrl;
        public string SiteUrl;

        public static PfxAccount Parse(string json)
        {
            var o = PfxJson.ParseObject(json);
            return new PfxAccount
            {
                Plan = PfxJson.GetString(o, "plan", "free"),
                HasPlan = PfxJson.GetBool(o, "has_plan", false),
                ExportsLeftToday = PfxJson.GetNullableInt(o, "exports_left_today"),
                ExportsPerDay = PfxJson.GetNullableInt(o, "exports_per_day"),
                PricingUrl = PfxJson.GetString(o, "pricing_url", null),
                SiteUrl = PfxJson.GetString(o, "site_url", null),
            };
        }

        public string PlanDisplayName
        {
            get
            {
                switch (Plan)
                {
                    case "free": return "Free";
                    case "starter": return "Starter";
                    case "creator": return "Creator";
                    case "studio": return "Studio";
                    case "studio_max": return "Studio Max";
                    default: return string.IsNullOrEmpty(Plan) ? "Free" : Plan;
                }
            }
        }

        /// <summary>The one line account summary shown in the window.</summary>
        public string Summary
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append("Plan: ").Append(PlanDisplayName).Append(". ");
                if (ExportsLeftToday == null) sb.Append("Unlimited imports.");
                else
                {
                    sb.Append("Imports left today: ").Append(ExportsLeftToday.Value.ToString(CultureInfo.InvariantCulture));
                    if (ExportsPerDay != null) sb.Append(" of ").Append(ExportsPerDay.Value.ToString(CultureInfo.InvariantCulture));
                    sb.Append('.');
                }
                return sb.ToString();
            }
        }

        /// <summary>Applies the X-PFX-Exports-Left header from an import ("unlimited" or a number).</summary>
        public void ApplyExportsLeftHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return;
            if (string.Equals(header.Trim(), "unlimited", StringComparison.OrdinalIgnoreCase)) { ExportsLeftToday = null; return; }
            int n;
            if (int.TryParse(header.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) ExportsLeftToday = n;
        }
    }

    public sealed class PfxEffectItem
    {
        public string Id;
        public string Slug;
        public string Title;
        public string Category;
        public List<string> Tags = new List<string>();
        public string Description;
        public double Duration;
        public bool Loop;
        public bool PixelArt;
        public bool Premium;
        /// <summary>WebP poster (Unity cannot decode it; kept as a last resort).</summary>
        public string Poster;
        /// <summary>PNG poster (preferred: ImageConversion.LoadImage reads PNG).</summary>
        public string PosterPng;
        public string Page;
        public string UpdatedAt;
        /// <summary>true for the user's own effects (My effects tab).</summary>
        public bool Own;

        /// <summary>The best poster URL Unity can try to decode, or null.</summary>
        public string BestPosterUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(PosterPng)) return PosterPng;
                if (!string.IsNullOrEmpty(Poster) && !Poster.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return Poster;
                return null;
            }
        }

        internal static PfxEffectItem FromJson(Dictionary<string, object> o, bool own)
        {
            return new PfxEffectItem
            {
                Id = PfxJson.GetString(o, "id", ""),
                Slug = PfxJson.GetString(o, "slug", null),
                Title = PfxJson.GetString(o, "title", "Untitled"),
                Category = PfxJson.GetString(o, "category", null),
                Tags = PfxJson.GetStringList(o, "tags"),
                Description = PfxJson.GetString(o, "description", null),
                Duration = PfxJson.GetDouble(o, "duration", 0),
                Loop = PfxJson.GetBool(o, "loop", false),
                PixelArt = PfxJson.GetBool(o, "pixel_art", false),
                Premium = PfxJson.GetBool(o, "premium", false),
                Poster = PfxJson.GetString(o, "poster", null),
                PosterPng = PfxJson.GetString(o, "poster_png", null),
                Page = PfxJson.GetString(o, "page", null),
                UpdatedAt = PfxJson.GetString(o, "updated_at", null),
                Own = own,
            };
        }
    }

    public sealed class PfxEffectPage
    {
        public int Total;
        public List<string> Categories = new List<string>();
        public List<PfxEffectItem> Items = new List<PfxEffectItem>();

        public static PfxEffectPage Parse(string json, bool own)
        {
            var o = PfxJson.ParseObject(json);
            var page = new PfxEffectPage
            {
                Total = PfxJson.GetInt(o, "total", 0),
                Categories = PfxJson.GetStringList(o, "categories"),
            };
            var items = PfxJson.GetArray(o, "items");
            if (items != null)
            {
                foreach (var it in items)
                {
                    var io = it as Dictionary<string, object>;
                    if (io != null) page.Items.Add(PfxEffectItem.FromJson(io, own));
                }
            }
            return page;
        }
    }

    /// <summary>An API error ready to show: a sentence, and a pricing link for 402.</summary>
    public sealed class PfxApiError
    {
        public long Status;
        public string Code;
        public string Description;
        public string UpgradeUrl;
        /// <summary>Seconds to wait before retrying (429), when known.</summary>
        public int? RetryAfterSeconds;

        public bool IsAuth { get { return Status == 401; } }
        public bool IsPaywall { get { return Status == 402; } }

        public override string ToString() { return Description; }

        public static PfxApiError Network(string apiBase, string detail)
        {
            return new PfxApiError
            {
                Status = 0,
                Code = "network",
                Description = "Could not reach " + apiBase + ". Check your connection. (" + (detail ?? "no response") + ")",
            };
        }

        /// <summary>Builds an error from an HTTP status, the response body and two optional headers.</summary>
        public static PfxApiError FromResponse(long status, string body, string retryAfterHeader, string rateLimitResetHeader, string defaultPricingUrl)
        {
            var err = new PfxApiError { Status = status };
            Dictionary<string, object> o = null;
            if (!string.IsNullOrEmpty(body) && body.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                try { o = PfxJson.ParseObject(body); } catch (PfxJsonException) { o = null; }
            }
            if (o != null)
            {
                err.Code = PfxJson.GetString(o, "error", null);
                err.Description = PfxJson.GetString(o, "error_description", null);
                err.UpgradeUrl = PfxJson.GetString(o, "upgrade_url", null);
            }

            int secs;
            if (!string.IsNullOrEmpty(retryAfterHeader) && int.TryParse(retryAfterHeader.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out secs))
                err.RetryAfterSeconds = secs;
            else if (!string.IsNullOrEmpty(rateLimitResetHeader) && int.TryParse(rateLimitResetHeader.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out secs))
            {
                // X-RateLimit-Reset may be seconds-from-now or a unix time.
                if (secs > 1000000000)
                {
                    long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    secs = (int)Math.Max(0, secs - now);
                }
                err.RetryAfterSeconds = secs;
            }

            if (string.IsNullOrEmpty(err.Code)) err.Code = DefaultCode(status);
            if (string.IsNullOrEmpty(err.Description)) err.Description = DefaultDescription(status, err.RetryAfterSeconds);
            if (status == 402 && string.IsNullOrEmpty(err.UpgradeUrl)) err.UpgradeUrl = defaultPricingUrl;
            return err;
        }

        static string DefaultCode(long status)
        {
            switch (status)
            {
                case 400: return "bad_request";
                case 401: return "invalid_token";
                case 402: return "plan_required";
                case 404: return "not_found";
                case 429: return "rate_limited";
                case 503: return "temporarily_unavailable";
                default: return "http_" + status.ToString(CultureInfo.InvariantCulture);
            }
        }

        static string DefaultDescription(long status, int? retryAfter)
        {
            switch (status)
            {
                case 400: return "The request was not accepted. Update the ParticleFX Studio package and try again.";
                case 401: return "Your API key is missing, wrong or revoked. Get a new key from particlefx.studio/billing.";
                case 402: return "This needs a paid plan.";
                case 404: return "That effect was not found. It may have been removed.";
                case 429:
                    return retryAfter.HasValue
                        ? "Too many requests. Try again in " + retryAfter.Value.ToString(CultureInfo.InvariantCulture) + " seconds."
                        : "Too many requests. Wait a moment and try again.";
                case 503: return "ParticleFX Studio is temporarily unavailable. Try again in a minute.";
                default: return "The server answered with HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".";
            }
        }
    }

    public static class PfxApiRoutes
    {
        public const string DefaultApiBase = "https://mcp.particlefx.studio";
        public const string KeysUrl = "https://particlefx.studio/billing";
        public const string PricingUrl = "https://particlefx.studio/pricing";
        public const string SiteUrl = "https://particlefx.studio";
        public const int PageSize = 60;

        public static string NormalizeBase(string apiBase)
        {
            if (string.IsNullOrEmpty(apiBase)) return DefaultApiBase;
            apiBase = apiBase.Trim();
            while (apiBase.EndsWith("/", StringComparison.Ordinal)) apiBase = apiBase.Substring(0, apiBase.Length - 1);
            return apiBase.Length == 0 ? DefaultApiBase : apiBase;
        }

        public static string Me(string apiBase) { return NormalizeBase(apiBase) + "/v1/plugin/me"; }

        public static string Library(string apiBase, string query, string category, int offset, int limit)
        {
            var sb = new StringBuilder(NormalizeBase(apiBase)).Append("/v1/plugin/library?");
            sb.Append("q=").Append(Uri.EscapeDataString(query ?? ""));
            sb.Append("&category=").Append(Uri.EscapeDataString(category ?? ""));
            sb.Append("&offset=").Append(Math.Max(0, offset).ToString(CultureInfo.InvariantCulture));
            sb.Append("&limit=").Append(Clamp(limit, 1, 120).ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static string MyEffects(string apiBase, int offset, int limit)
        {
            return NormalizeBase(apiBase) + "/v1/plugin/my-effects?offset=" + Math.Max(0, offset).ToString(CultureInfo.InvariantCulture)
                + "&limit=" + Clamp(limit, 1, 120).ToString(CultureInfo.InvariantCulture);
        }

        public static string Import(string apiBase) { return NormalizeBase(apiBase) + "/v1/plugin/import"; }

        public static string ImportBody(string effectId, int scale, bool normalMap, bool packed)
        {
            var body = new Dictionary<string, object>
            {
                { "effect_id", effectId },
                { "engine", "unity" },
                { "scale", Math.Max(1, scale) },
                { "normal_map", normalMap },
                { "layout", packed ? "packed" : "grid" },
            };
            return PfxJson.Serialize(body);
        }

        /// <summary>The X-PFX-Client header value.</summary>
        public static string ClientHeader(string packageVersion, string unityVersion)
        {
            return "unity-package/" + packageVersion + " (Unity " + unityVersion + ")";
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }

    public static class PfxPaths
    {
        public const string DefaultTargetFolder = "Assets/ParticleFX";

        /// <summary>Lower case [a-z0-9_-] folder name from a slug or title.</summary>
        public static string SanitizeSlug(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return "effect";
            var sb = new StringBuilder();
            bool lastDash = false;
            foreach (var raw in slug.Trim().ToLowerInvariant())
            {
                char c = raw;
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (ok) { sb.Append(c); lastDash = c == '-'; }
                else if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
            }
            var s = sb.ToString().Trim('-');
            if (s.Length > 64) s = s.Substring(0, 64).Trim('-');
            return s.Length == 0 ? "effect" : s;
        }

        /// <summary>
        /// Normalises a project folder ("Assets/..." with forward slashes, no
        /// trailing slash). Returns null if it is not inside Assets.
        /// </summary>
        public static string NormalizeAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return DefaultTargetFolder;
            var f = folder.Trim().Replace('\\', '/');
            while (f.EndsWith("/", StringComparison.Ordinal)) f = f.Substring(0, f.Length - 1);
            if (f != "Assets" && !f.StartsWith("Assets/", StringComparison.Ordinal)) return null;
            foreach (var part in f.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == "..") return null;
            }
            return f;
        }

        /// <summary>
        /// Converts an absolute folder (from a folder picker) to an Assets path
        /// using the project's Assets directory. Returns null when outside it.
        /// </summary>
        public static string AbsoluteToAssetFolder(string absolute, string dataPath)
        {
            if (string.IsNullOrEmpty(absolute) || string.IsNullOrEmpty(dataPath)) return null;
            var a = absolute.Replace('\\', '/').TrimEnd('/');
            var d = dataPath.Replace('\\', '/').TrimEnd('/');
            if (string.Equals(a, d, StringComparison.OrdinalIgnoreCase)) return "Assets";
            if (a.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)) return NormalizeAssetFolder("Assets" + a.Substring(d.Length));
            return null;
        }
    }
}
