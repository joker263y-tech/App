using System;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Stats;

namespace Shadowbound.Core.Combat
{
    /// <summary>
    /// Health and stamina for one combatant.
    ///
    /// Maximums are read live from the <see cref="StatSet"/> rather than cached,
    /// so equipping armour or gaining a level immediately changes the ceiling.
    /// Health is never silently scaled when that ceiling moves; callers decide
    /// whether a new maximum should grant extra health (level up) or leave the
    /// current value clamped (armour swap).
    ///
    /// Raises events so the Unity layer can react with VFX, audio and UI without
    /// polling every frame.
    /// </summary>
    public sealed class Vitals
    {
        private readonly StatSet _stats;
        private readonly ResistanceSet _resistance;

        private float _health;
        private float _stamina;
        private bool _initialized;

        public Vitals(StatSet stats, ResistanceSet resistance)
        {
            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }

            _stats = stats;
            _resistance = resistance ?? new ResistanceSet();
            _health = 0f;
            _stamina = 0f;
            _initialized = false;
        }

        /// <summary>Fired with the amount actually removed and the source that caused it.</summary>
        public event Action<float, object> Damaged;

        public event Action<float> Healed;

        /// <summary>Fired once, when health reaches zero.</summary>
        public event Action Died;

        /// <summary>Fired when stamina is insufficient to cover a requested cost.</summary>
        public event Action StaminaDepleted;

        public event Action<float> StaminaSpent;

        public float Health
        {
            get { return _health; }
        }

        public float Stamina
        {
            get { return _stamina; }
        }

        public float MaxHealth
        {
            get { return _stats.Get(StatId.MaxHealth); }
        }

        public float MaxStamina
        {
            get { return _stats.Get(StatId.MaxStamina); }
        }

        public ResistanceSet Resistance
        {
            get { return _resistance; }
        }

        public float HealthFraction
        {
            get
            {
                float max = MaxHealth;
                return max <= 0f ? 0f : FMath.Clamp01(_health / max);
            }
        }

        public float StaminaFraction
        {
            get
            {
                float max = MaxStamina;
                return max <= 0f ? 0f : FMath.Clamp01(_stamina / max);
            }
        }

        public bool IsAlive
        {
            get { return _health > 0f; }
        }

        public bool IsInitialized
        {
            get { return _initialized; }
        }

        /// <summary>Fills health and stamina, and marks the pool usable. Called after stats are configured.</summary>
        public void ResetToFull()
        {
            _health = MaxHealth;
            _stamina = MaxStamina;
            _initialized = true;
        }

        /// <summary>Regenerates over time and clamps to current maximums.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (!_initialized)
            {
                return;
            }

            // A dead pool never regenerates.
            //
            // Without this guard, a corpse with any HealthRegen stat climbs
            // back above zero and comes back to life on the next Tick, because
            // the regeneration branch only asks "is health below maximum?".
            // Death must be a terminal state until something explicitly
            // revives the combatant.
            if (_health <= 0f)
            {
                return;
            }

            float maxHealth = MaxHealth;
            float maxStamina = MaxStamina;

            if (_health < maxHealth)
            {
                float regen = _stats.Get(StatId.HealthRegen);
                if (regen > 0f)
                {
                    _health = FMath.Clamp(_health + (regen * deltaTime), 0f, maxHealth);
                }
            }

            if (_stamina < maxStamina)
            {
                float regen = _stats.Get(StatId.StaminaRegen);
                if (regen > 0f)
                {
                    _stamina = FMath.Clamp(_stamina + (regen * deltaTime), 0f, maxStamina);
                }
            }

            // A maximum that shrank (armour removed) must pull the current value down.
            if (_health > maxHealth)
            {
                _health = maxHealth;
            }

            if (_stamina > maxStamina)
            {
                _stamina = maxStamina;
            }
        }

        /// <summary>
        /// Removes health and returns the amount actually applied, which is less
        /// than requested when the blow is lethal. Returns 0 for a corpse, so
        /// overkill damage cannot be re-counted by a second attacker.
        /// </summary>
        public float ApplyDamage(float amount, object source)
        {
            if (amount <= 0f || !_initialized || !IsAlive)
            {
                return 0f;
            }

            bool wasAlive = IsAlive;
            float applied = amount > _health ? _health : amount;
            _health -= applied;

            Damaged?.Invoke(applied, source);

            if (wasAlive && !IsAlive)
            {
                Died?.Invoke();
            }

            return applied;
        }

        /// <summary>Restores health up to the maximum and returns the amount actually restored.</summary>
        public float Heal(float amount)
        {
            if (amount <= 0f || !_initialized || !IsAlive)
            {
                return 0f;
            }

            float max = MaxHealth;
            if (_health >= max)
            {
                return 0f;
            }

            float restored = amount > max - _health ? max - _health : amount;
            _health += restored;
            Healed?.Invoke(restored);
            return restored;
        }

        /// <summary>
        /// Spends stamina if the full cost is available. Never partially spends:
        /// a failed check leaves the pool untouched, which keeps ability
        /// affordability predictable.
        /// </summary>
        public bool TrySpendStamina(float cost)
        {
            if (cost <= 0f)
            {
                return true;
            }

            if (_stamina < cost)
            {
                StaminaDepleted?.Invoke();
                return false;
            }

            _stamina -= cost;
            StaminaSpent?.Invoke(cost);
            return true;
        }

        public void RestoreStamina(float amount)
        {
            if (amount <= 0f || !_initialized)
            {
                return;
            }

            float max = MaxStamina;
            _stamina = FMath.Clamp(_stamina + amount, 0f, max);
        }

        /// <summary>Removes all health without raising <see cref="Died"/>. Used when resetting an encounter.</summary>
        public void KillSilently()
        {
            _health = 0f;
        }
    }
}
