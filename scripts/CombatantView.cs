using System;
using Godot;
using Shadowbound.Core.Combat;

namespace Shadowbound.Game
{
    /// <summary>
    /// Mirrors one core <see cref="Combatant"/> onto a Godot node.
    ///
    /// The core owns position and facing; this node only copies them onto its
    /// transform and reacts to events. It never moves the combatant itself, so
    /// there is exactly one authority over where anything is.
    ///
    /// Hit feedback the core cannot know about lives here: a colour flash and a
    /// scale punch, both driven by the Damaged event rather than by polling
    /// health, so a hit is visible even if it is immediately fatal.
    /// </summary>
    public partial class CombatantView : Node3D
    {
        public Combatant Combatant { get; private set; }

        /// <summary>Raised after this view has reacted to a hit, for camera shake.</summary>
        public event Action<DamageResult> Hit;

        /// <summary>Raised once, when the underlying combatant dies.</summary>
        public event Action Died;

        protected MeshInstance3D Body;

        private StandardMaterial3D _material;
        private Color _baseColor = Colors.White;
        private float _bodyScale = 1f;
        private float _flashRemaining;
        private float _punchRemaining;
        private bool _deathHandled;

        private const float FlashDuration = 0.12f;
        private const float PunchDuration = 0.12f;

        /// <summary>Chooses which mesh is the body and how high its centre sits.</summary>
        protected void AttachBody(MeshInstance3D body, float meshHeight)
        {
            Body = body;
            _material = new StandardMaterial3D
            {
                AlbedoColor = _baseColor,
                Roughness = 0.85f,
                Metallic = 0f
            };
            Body.MaterialOverride = _material;
            Body.Position = new Vector3(0f, meshHeight, 0f);
        }

        public void Bind(Combatant combatant, Color tint, float bodyScale)
        {
            Unbind();

            Combatant = combatant;
            _baseColor = tint;
            _bodyScale = Mathf.Max(0.1f, bodyScale);
            _deathHandled = false;

            Scale = Vector3.One * _bodyScale;
            ApplyTint(tint);

            combatant.Damaged += OnDamaged;
            combatant.Died += OnDied;

            Sync(0f);
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

        public override void _ExitTree()
        {
            Unbind();
        }

        /// <summary>Copies the core transform and decays hit feedback. Called once per physics step.</summary>
        public virtual void Sync(float deltaTime)
        {
            if (Combatant == null)
            {
                return;
            }

            var position = Combatant.Position;
            Position = new Vector3(position.X, position.Y, position.Z);
            Rotation = new Vector3(0f, CoordinateConvert.FacingToYawRadians(Combatant.FacingDegrees), 0f);

            if (deltaTime > 0f)
            {
                _flashRemaining = Mathf.Max(0f, _flashRemaining - deltaTime);
                _punchRemaining = Mathf.Max(0f, _punchRemaining - deltaTime);
            }

            // A dead combatant stops taking hits, so clear the flash immediately.
            if (!Combatant.IsAlive)
            {
                _flashRemaining = 0f;
            }

            float t = FlashDuration <= 0f ? 0f : Mathf.Clamp(_flashRemaining / FlashDuration, 0f, 1f);
            ApplyTint(t > 0f ? _baseColor.Lerp(Colors.White, t) : _baseColor);

            if (Body != null)
            {
                float punch = _punchRemaining > 0f
                    ? 1f + (0.12f * Mathf.Clamp(_punchRemaining / PunchDuration, 0f, 1f))
                    : 1f;
                Body.Scale = Vector3.One * punch;
            }
        }

        /// <summary>Resets flash state, for encounter resets and region travel.</summary>
        public void ClearFeedback()
        {
            _flashRemaining = 0f;
            _punchRemaining = 0f;
            _deathHandled = false;
        }

        protected void ApplyTint(Color tint)
        {
            if (_material != null)
            {
                _material.AlbedoColor = tint;
            }
        }

        private void OnDamaged(Combatant victim, Combatant attacker, DamageResult result)
        {
            _flashRemaining = FlashDuration;
            _punchRemaining = PunchDuration;
            Hit?.Invoke(result);
        }

        private void OnDied(Combatant victim)
        {
            if (_deathHandled)
            {
                return;
            }

            _deathHandled = true;

            // Corpses read as grey rather than their living tint.
            ApplyTint(new Color(0.25f, 0.24f, 0.24f));
            Died?.Invoke();
        }
    }
}
