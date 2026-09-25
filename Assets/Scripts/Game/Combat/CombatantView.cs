using System;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using UnityEngine;

namespace Shadowbound.Game.Combat
{
    /// <summary>
    /// Mirrors one core <see cref="Combatant"/> onto a GameObject.
    ///
    /// The core simulation owns position and facing; this component only copies
    /// them onto a transform and reacts to events. It never moves the combatant
    /// itself, so there is exactly one authority over where anything is.
    ///
    /// Also supplies the hit feedback the core cannot know about: a colour flash
    /// and a scale punch, both driven by the Damaged event rather than by polling
    /// health, so a hit is visible even if it is immediately fatal.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatantView : MonoBehaviour
    {
        /// <summary>Height above the core position at which the body is drawn.</summary>
        public float VerticalOffset = 1f;

        /// <summary>Seconds a hit flash lasts.</summary>
        public float FlashDuration = 0.12f;

        public Combatant Combatant { get; private set; }

        /// <summary>Fired after this view has reacted to a hit, for camera shake and audio.</summary>
        public event Action<DamageResult> Hit;

        /// <summary>Fired once when the underlying combatant dies.</summary>
        public event Action Died;

        private Renderer _renderer;
        private MaterialPropertyBlock _block;
        private Color _baseColor = Color.white;
        private float _flashRemaining;
        private float _punchRemaining;
        private float _bodyScale = 1f;
        private bool _deathHandled;

        /// <summary>Total frames this view has been visible. Diagnostics only.</summary>
        public int FramesVisible { get; private set; }

        public void Bind(Combatant combatant, Renderer targetRenderer, float bodyScale)
        {
            Unbind();

            Combatant = combatant;
            _renderer = targetRenderer;
            _bodyScale = Mathf.Max(0.1f, bodyScale);
            _block = new MaterialPropertyBlock();

            if (_renderer != null && _renderer.sharedMaterial != null)
            {
                Material shared = _renderer.sharedMaterial;
                _baseColor = shared.HasProperty("_BaseColor")
                    ? shared.GetColor("_BaseColor")
                    : shared.color;

                // Per-object colour via a property block, so every instance sharing
                // a material does not need its own copy of it.
                _block.SetColor("_BaseColor", _baseColor);
                _block.SetColor("_Color", _baseColor);
                _renderer.SetPropertyBlock(_block);
            }

            combatant.Damaged += OnDamaged;
            combatant.Died += OnDied;
        }

        public void Unbind()
        {
            if (Combatant != null)
            {
                Combatant.Damaged -= OnDamaged;
                Combatant.Died -= OnDied;
            }

            Combatant = null;
        }

        private void OnDestroy()
        {
            Unbind();
        }

        /// <summary>Copies the core transform and decays hit feedback. Called once per frame.</summary>
        public void Sync(float deltaTime)
        {
            if (Combatant == null)
            {
                return;
            }

            FramesVisible++;

            Float3 position = Combatant.Position;
            transform.position = new Vector3(position.X, position.Y + VerticalOffset, position.Z);
            transform.rotation = Quaternion.Euler(0f, Combatant.FacingDegrees, 0f);

            bool wasPunching = _punchRemaining > 0f;

            if (deltaTime > 0f)
            {
                _flashRemaining = Mathf.Max(0f, _flashRemaining - deltaTime);
                _punchRemaining = Mathf.Max(0f, _punchRemaining - deltaTime);
            }

            if (_renderer != null && _block != null)
            {
                float t = FlashDuration <= 0f ? 0f : _flashRemaining / FlashDuration;
                _block.SetColor("_BaseColor", Color.Lerp(_baseColor, Color.white, t));
                _block.SetColor("_Color", Color.Lerp(_baseColor, Color.white, t));
                _renderer.SetPropertyBlock(_block);
            }

            if (wasPunching || _punchRemaining > 0f)
            {
                float punch = 1f + (0.12f * Mathf.Clamp01(_punchRemaining / 0.12f));
                transform.localScale = Vector3.one * (_bodyScale * punch);
            }
            else
            {
                transform.localScale = Vector3.one * _bodyScale;
            }

            // A dead combatant stops taking hits, so clear the flash immediately.
            if (!Combatant.IsAlive && _flashRemaining > 0f)
            {
                _flashRemaining = 0f;
            }
        }

        private void OnDamaged(Combatant victim, Combatant attacker, DamageResult result)
        {
            _flashRemaining = FlashDuration;
            _punchRemaining = 0.12f;

            Hit?.Invoke(result);
        }

        private void OnDied(Combatant victim)
        {
            if (_deathHandled)
            {
                return;
            }

            _deathHandled = true;
            Died?.Invoke();
        }

        /// <summary>Resets flash state, for encounter resets.</summary>
        public void ClearFeedback()
        {
            _flashRemaining = 0f;
            _punchRemaining = 0f;
            _deathHandled = false;
        }
    }
}
