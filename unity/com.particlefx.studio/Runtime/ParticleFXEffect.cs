// ParticleFX Studio for Unity. MIT licensed.
using System;
using UnityEngine;
using UnityEngine.Events;

namespace ParticleFX.Studio
{
    /// <summary>
    /// Plays an imported ParticleFX Studio effect (a sprite animation driven by
    /// an Animator) and handles one-shot clean up.
    /// </summary>
    /// <remarks>
    /// Spawn a one-shot with <c>Instantiate(prefab, position, rotation)</c>; with
    /// <see cref="FinishAction.Destroy"/> it removes itself after its last frame.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("ParticleFX Studio/ParticleFX Effect")]
    [HelpURL("https://particlefx.studio")]
    public class ParticleFXEffect : MonoBehaviour
    {
        /// <summary>What a one-shot does after its last frame. Looping effects never finish.</summary>
        public enum FinishAction
        {
            /// <summary>Keep showing the last frame.</summary>
            KeepLastFrame = 0,
            /// <summary>Hide the SpriteRenderer (Restart shows it again).</summary>
            Hide = 1,
            /// <summary>Deactivate the GameObject (useful for pooling).</summary>
            Deactivate = 2,
            /// <summary>Destroy the GameObject.</summary>
            Destroy = 3,
        }

        [Tooltip("Restart the effect every time the GameObject is enabled.")]
        [SerializeField] bool playOnEnable = true;

        [Tooltip("What a one-shot effect does after its last frame. Looping effects never finish.")]
        [SerializeField] FinishAction whenFinished = FinishAction.Destroy;

        [Tooltip("Playback speed multiplier.")]
        [SerializeField, Min(0f)] float speed = 1f;

        [Tooltip("Invoked once when a one-shot effect plays its last frame.")]
        [SerializeField] UnityEvent onFinished = new UnityEvent();

        // Filled in by the importer; informational at runtime.
        [SerializeField, HideInInspector] string stateName = "";
        [SerializeField] string effectId = "";
        [SerializeField] bool loop = false;
        [SerializeField] float duration = 0f;
        [SerializeField] int frameCount = 0;
        [SerializeField] float framesPerSecond = 0f;
        [SerializeField] string sourceUrl = "";

        Animator _animator;
        SpriteRenderer _renderer;
        bool _playing;
        bool _finished;

        /// <summary>Raised once when a one-shot effect plays its last frame.</summary>
        public event Action<ParticleFXEffect> Finished;

        public bool IsPlaying { get { return _playing && !_finished; } }
        public bool IsFinished { get { return _finished; } }
        public bool Loop { get { return loop; } }
        /// <summary>Length of one play-through in seconds at speed 1.</summary>
        public float Duration { get { return duration; } }
        public int FrameCount { get { return frameCount; } }
        public float FramesPerSecond { get { return framesPerSecond; } }
        public string EffectId { get { return effectId; } }
        public string SourceUrl { get { return sourceUrl; } }
        public UnityEvent OnFinished { get { return onFinished; } }

        public FinishAction WhenFinished
        {
            get { return whenFinished; }
            set { whenFinished = value; }
        }

        public bool PlayOnEnable
        {
            get { return playOnEnable; }
            set { playOnEnable = value; }
        }

        public float Speed
        {
            get { return speed; }
            set
            {
                speed = Mathf.Max(0f, value);
                if (_animator != null && _playing) _animator.speed = speed;
            }
        }

        Animator Anim
        {
            get
            {
                if (_animator == null) _animator = GetComponent<Animator>();
                return _animator;
            }
        }

        SpriteRenderer Renderer
        {
            get
            {
                if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
                return _renderer;
            }
        }

        void OnEnable()
        {
            if (playOnEnable) Restart();
            else
            {
                // Hold the first frame until Play is called.
                _playing = false;
                if (Anim != null) Anim.speed = 0f;
            }
        }

        /// <summary>Resumes playback (or starts it if it was never started).</summary>
        public void Play()
        {
            if (_finished) { Restart(); return; }
            var anim = Anim;
            if (anim == null) return;
            if (Renderer != null) Renderer.enabled = true;
            anim.enabled = true;
            anim.speed = speed;
            _playing = true;
        }

        /// <summary>Plays from the first frame.</summary>
        public void Restart()
        {
            var anim = Anim;
            if (anim == null) return;
            if (Renderer != null) Renderer.enabled = true;
            anim.enabled = true;
            anim.speed = speed;
            _finished = false;
            _playing = true;
            if (anim.runtimeAnimatorController == null) return;
            // stateName is the full path ("Base Layer.Fireball"); its hash is the
            // state's fullPathHash, which HasState and Play both accept.
            int hash = string.IsNullOrEmpty(stateName) ? 0 : Animator.StringToHash(stateName);
            if (hash != 0 && anim.HasState(0, hash))
                anim.Play(hash, 0, 0f);
            else
                anim.Play(anim.GetCurrentAnimatorStateInfo(0).fullPathHash, 0, 0f);
        }

        /// <summary>Freezes on the current frame.</summary>
        public void Pause()
        {
            _playing = false;
            if (Anim != null) Anim.speed = 0f;
        }

        /// <summary>Stops and hides the effect. Restart or Play shows it again.</summary>
        public void Stop()
        {
            Pause();
            if (Renderer != null) Renderer.enabled = false;
        }

        void Update()
        {
            if (!_playing || _finished || loop) return;
            var anim = Anim;
            if (anim == null || anim.runtimeAnimatorController == null || !anim.isActiveAndEnabled) return;
            if (anim.IsInTransition(0)) return;
            var info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.loop) return;
            if (info.normalizedTime >= 1f) Finish();
        }

        void Finish()
        {
            _finished = true;
            _playing = false;
            if (Finished != null) Finished(this);
            if (onFinished != null) onFinished.Invoke();
            if (this == null) return; // a listener destroyed us
            switch (whenFinished)
            {
                case FinishAction.Hide:
                    if (Renderer != null) Renderer.enabled = false;
                    break;
                case FinishAction.Deactivate:
                    gameObject.SetActive(false);
                    break;
                case FinishAction.Destroy:
                    Destroy(gameObject);
                    break;
            }
        }

#if UNITY_EDITOR
        /// <summary>Editor only: called by the importer to record the effect's data.</summary>
        public void EditorConfigure(string effectId, string stateName, bool loop, float duration, int frameCount, float fps,
            string sourceUrl, bool resetFinishAction)
        {
            this.effectId = effectId ?? "";
            this.stateName = stateName ?? "";
            this.loop = loop;
            this.duration = duration;
            this.frameCount = frameCount;
            this.framesPerSecond = fps;
            this.sourceUrl = sourceUrl ?? "";
            if (resetFinishAction) whenFinished = loop ? FinishAction.KeepLastFrame : FinishAction.Destroy;
        }
#endif
    }
}
