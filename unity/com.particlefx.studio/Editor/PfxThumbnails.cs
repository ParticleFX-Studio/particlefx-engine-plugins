// ParticleFX Studio for Unity. MIT licensed.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ParticleFX.Studio.Editor
{
    /// <summary>
    /// Downloads and caches poster thumbnails, a few at a time. Posters must be
    /// PNG or JPG (ImageConversion.LoadImage cannot decode WebP); a poster that
    /// fails to load is remembered as failed and the card shows its title only.
    /// </summary>
    internal sealed class PfxThumbnails : IDisposable
    {
        const int MaxConcurrent = 4;
        const int MaxCached = 400;

        readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        readonly HashSet<string> _failed = new HashSet<string>();
        readonly HashSet<string> _loading = new HashSet<string>();
        readonly List<string> _queue = new List<string>();
        readonly List<string> _order = new List<string>();
        readonly Action _repaint;
        int _inFlight;

        public PfxThumbnails(Action repaint) { _repaint = repaint; }

        /// <summary>The texture if loaded; otherwise queues it and returns null.</summary>
        public Texture2D Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            Texture2D tex;
            if (_textures.TryGetValue(url, out tex) && tex != null) return tex;
            if (_failed.Contains(url) || _loading.Contains(url)) return null;
            _loading.Add(url);
            _queue.Add(url);
            Next();
            return null;
        }

        public bool Failed(string url)
        {
            return string.IsNullOrEmpty(url) || _failed.Contains(url);
        }

        /// <summary>Drops queued (not yet started) downloads, e.g. when the result list changes.</summary>
        public void ClearQueue()
        {
            foreach (var url in _queue) _loading.Remove(url);
            _queue.Clear();
        }

        void Next()
        {
            while (_inFlight < MaxConcurrent && _queue.Count > 0)
            {
                // Newest requests first: those are the cards on screen now.
                var url = _queue[_queue.Count - 1];
                _queue.RemoveAt(_queue.Count - 1);
                _inFlight++;
                PfxApiClient.Image(this, url, (bytes, error) =>
                {
                    _inFlight--;
                    _loading.Remove(url);
                    if (bytes == null || !Store(url, bytes)) _failed.Add(url);
                    if (_repaint != null) _repaint();
                    Next();
                });
            }
        }

        bool Store(string url, byte[] bytes)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "ParticleFX poster",
            };
            bool ok;
            try { ok = tex.LoadImage(bytes, true); }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return false;
            }
            _textures[url] = tex;
            _order.Add(url);
            while (_order.Count > MaxCached)
            {
                var old = _order[0];
                _order.RemoveAt(0);
                Texture2D t;
                if (_textures.TryGetValue(old, out t) && t != null) UnityEngine.Object.DestroyImmediate(t);
                _textures.Remove(old);
            }
            return true;
        }

        public void Dispose()
        {
            PfxHttp.CancelAll(this);
            foreach (var t in _textures.Values)
                if (t != null) UnityEngine.Object.DestroyImmediate(t);
            _textures.Clear();
            _order.Clear();
            _queue.Clear();
            _loading.Clear();
            _inFlight = 0;
        }
    }
}
