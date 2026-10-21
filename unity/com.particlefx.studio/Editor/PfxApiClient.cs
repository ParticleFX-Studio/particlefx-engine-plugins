// ParticleFX Studio for Unity. MIT licensed.
using System;
using System.Collections.Generic;
using System.Text;
using ParticleFX.Studio.Editor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace ParticleFX.Studio.Editor
{
    /// <summary>A finished request: success data, or an error ready to show.</summary>
    internal sealed class PfxResponse
    {
        public long Status;
        public byte[] Data;
        public PfxApiError Error;
        public Dictionary<string, string> Headers;

        public bool Ok { get { return Error == null; } }

        public string Text
        {
            get { return Data == null ? "" : Encoding.UTF8.GetString(Data); }
        }

        public string Header(string name)
        {
            if (Headers == null) return null;
            foreach (var kv in Headers)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }
    }

    /// <summary>
    /// Sends UnityWebRequests and polls them from EditorApplication.update, so
    /// the editor never blocks. Callbacks run on the main thread.
    /// </summary>
    internal static class PfxHttp
    {
        sealed class Pending
        {
            public UnityWebRequest Request;
            public Action<PfxResponse> Done;
            public object Owner;
        }

        static readonly List<Pending> s_pending = new List<Pending>();
        static bool s_hooked;

        public static int PendingCount { get { return s_pending.Count; } }

        public static void Send(UnityWebRequest request, object owner, Action<PfxResponse> done)
        {
            s_pending.Add(new Pending { Request = request, Done = done, Owner = owner });
            request.SendWebRequest();
            if (!s_hooked)
            {
                EditorApplication.update += Pump;
                s_hooked = true;
            }
        }

        /// <summary>Aborts every request started by owner (no callbacks).</summary>
        public static void CancelAll(object owner)
        {
            for (int i = s_pending.Count - 1; i >= 0; i--)
            {
                var p = s_pending[i];
                if (!ReferenceEquals(p.Owner, owner)) continue;
                s_pending.RemoveAt(i);
                try { p.Request.Abort(); } catch (Exception) { }
                p.Request.Dispose();
            }
        }

        static void Pump()
        {
            for (int i = s_pending.Count - 1; i >= 0; i--)
            {
                var p = s_pending[i];
                if (!p.Request.isDone) continue;
                s_pending.RemoveAt(i);
                PfxResponse response;
                try { response = ToResponse(p.Request); }
                finally { p.Request.Dispose(); }
                try { p.Done(response); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (s_pending.Count == 0 && s_hooked)
            {
                EditorApplication.update -= Pump;
                s_hooked = false;
            }
        }

        static PfxResponse ToResponse(UnityWebRequest req)
        {
            var r = new PfxResponse
            {
                Status = req.responseCode,
                Data = req.downloadHandler != null ? req.downloadHandler.data : null,
                Headers = req.GetResponseHeaders(),
            };
            switch (req.result)
            {
                case UnityWebRequest.Result.Success:
                    break;
                case UnityWebRequest.Result.ProtocolError:
                    r.Error = PfxApiError.FromResponse(req.responseCode, r.Text,
                        r.Header("Retry-After"), r.Header("X-RateLimit-Reset"), PfxApiRoutes.PricingUrl);
                    break;
                default:
                    // ConnectionError, DataProcessingError, or an aborted request.
                    r.Error = PfxApiError.Network(HostOf(req.url), req.error);
                    break;
            }
            return r;
        }

        static string HostOf(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host : url;
        }
    }

    /// <summary>The ParticleFX plugin API (docs/ENGINE_PLUGINS.md).</summary>
    internal static class PfxApiClient
    {
        static string s_clientHeader;

        static string ClientHeader
        {
            get
            {
                if (s_clientHeader == null)
                    s_clientHeader = PfxApiRoutes.ClientHeader(PfxSettings.PackageVersion, Application.unityVersion);
                return s_clientHeader;
            }
        }

        static UnityWebRequest Get(string url, bool withKey, int timeoutSeconds)
        {
            var req = UnityWebRequest.Get(url);
            Prepare(req, withKey, timeoutSeconds);
            req.SetRequestHeader("Accept", "application/json");
            return req;
        }

        static void Prepare(UnityWebRequest req, bool withKey, int timeoutSeconds)
        {
            req.timeout = timeoutSeconds;
            req.SetRequestHeader("X-PFX-Client", ClientHeader);
            var key = PfxSettings.ApiKey;
            if (withKey && !string.IsNullOrEmpty(key)) req.SetRequestHeader("Authorization", "Bearer " + key);
        }

        public static void Me(object owner, Action<PfxAccount, PfxApiError> done)
        {
            var req = Get(PfxApiRoutes.Me(PfxSettings.ApiBase), true, 20);
            PfxHttp.Send(req, owner, r =>
            {
                if (!r.Ok) { done(null, r.Error); return; }
                PfxAccount account;
                try { account = PfxAccount.Parse(r.Text); }
                catch (PfxJsonException e) { done(null, BadJson(e)); return; }
                done(account, null);
            });
        }

        public static void Library(object owner, string query, string category, int offset, Action<PfxEffectPage, PfxApiError> done)
        {
            var url = PfxApiRoutes.Library(PfxSettings.ApiBase, query, category, offset, PfxApiRoutes.PageSize);
            // The key is optional here; sending it lists FX pack effects the account owns.
            PfxHttp.Send(Get(url, true, 20), owner, r => ParsePage(r, false, done));
        }

        public static void MyEffects(object owner, int offset, Action<PfxEffectPage, PfxApiError> done)
        {
            var url = PfxApiRoutes.MyEffects(PfxSettings.ApiBase, offset, PfxApiRoutes.PageSize);
            PfxHttp.Send(Get(url, true, 20), owner, r => ParsePage(r, true, done));
        }

        static void ParsePage(PfxResponse r, bool own, Action<PfxEffectPage, PfxApiError> done)
        {
            if (!r.Ok) { done(null, r.Error); return; }
            PfxEffectPage page;
            try { page = PfxEffectPage.Parse(r.Text, own); }
            catch (PfxJsonException e) { done(null, BadJson(e)); return; }
            done(page, null);
        }

        /// <summary>POST /v1/plugin/import. On success: the zip bytes and X-PFX-Exports-Left.</summary>
        public static void Import(object owner, string effectId, int scale, bool normalMap, bool packed,
            Action<byte[], string, PfxApiError> done)
        {
            var body = Encoding.UTF8.GetBytes(PfxApiRoutes.ImportBody(effectId, scale, normalMap, packed));
            // UnityWebRequest.Post(url, string, contentType) only exists from 2022.2, so build it by hand.
            var req = new UnityWebRequest(PfxApiRoutes.Import(PfxSettings.ApiBase), UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" },
                downloadHandler = new DownloadHandlerBuffer(),
            };
            Prepare(req, true, 180);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/zip, application/json");
            PfxHttp.Send(req, owner, r =>
            {
                if (!r.Ok) { done(null, null, r.Error); return; }
                var type = r.Header("Content-Type") ?? "";
                if (type.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    done(null, null, PfxApiError.FromResponse(r.Status, r.Text, null, null, PfxApiRoutes.PricingUrl));
                    return;
                }
                done(r.Data, r.Header("X-PFX-Exports-Left"), null);
            });
        }

        /// <summary>Downloads an image (poster). No API key is sent to image URLs.</summary>
        public static void Image(object owner, string url, Action<byte[], PfxApiError> done)
        {
            var req = UnityWebRequest.Get(url);
            // My effects posters need the key; it only ever goes to the API itself, never to another host.
            var apiBase = PfxSettings.ApiBase.TrimEnd('/') + "/";
            Prepare(req, url.StartsWith(apiBase, StringComparison.Ordinal), 30);
            PfxHttp.Send(req, owner, r => done(r.Ok ? r.Data : null, r.Error));
        }

        static PfxApiError BadJson(Exception e)
        {
            return new PfxApiError
            {
                Status = 0,
                Code = "bad_response",
                Description = "The server sent a response this package could not read. Update the ParticleFX Studio package. (" + e.Message + ")",
            };
        }
    }
}
