namespace Shadowbound.Core.Combat
{
    /// <summary>
    /// Damage schools in the game's original cosmology.
    /// Append new members at the end; values index arrays.
    /// </summary>
    public enum DamageType
    {
        /// <summary>Steel and impact. The baseline school.</summary>
        Physical = 0,

        /// <summary>Umbra. The player's primary offensive school.</summary>
        Shadow = 1,

        /// <summary>Fire and burning light.</summary>
        Ember = 2,

        /// <summary>Cold and stillness.</summary>
        Frost = 3,

        /// <summary>Direct life drain. Resisted by very little.</summary>
        Vital = 4
    }

    public static class DamageTypes
    {
        public const int Count = 5;

        public static readonly DamageType[] All =
        {
            DamageType.Physical,
            DamageType.Shadow,
            DamageType.Ember,
            DamageType.Frost,
            DamageType.Vital
        };

        public static string Name(DamageType type)
        {
            switch (type)
            {
                case DamageType.Physical: return "Physical";
                case DamageType.Shadow: return "Umbra";
                case DamageType.Ember: return "Ember";
                case DamageType.Frost: return "Frost";
                case DamageType.Vital: return "Vital";
                default: return type.ToString();
            }
        }
    }

    /// <summary>
    /// Per-school resistance as a 0..1 damage reduction fraction. Kept separate
    /// from <see cref="Stats.StatSet"/> because resistances are per-type and the
    /// stat system is not.
    /// </summary>
    public sealed class ResistanceSet
    {
        private readonly float[] _values;

        public ResistanceSet()
        {
            _values = new float[DamageTypes.Count];
        }

        public ResistanceSet(float uniformValue)
            : this()
        {
            for (int i = 0; i < DamageTypes.Count; i++)
            {
                _values[i] = uniformValue;
            }
        }

        public float Get(DamageType type)
        {
            int index = (int)type;
            return index >= 0 && index < DamageTypes.Count ? _values[index] : 0f;
        }

        /// <summary>Sets resistance. Values are clamped to -1..0.9, so nothing is fully immune.</summary>
        public void Set(DamageType type, float value)
        {
            int index = (int)type;
            if (index < 0 || index >= DamageTypes.Count)
            {
                return;
            }

            _values[index] = ClampResistance(value);
        }

        public void Add(DamageType type, float delta)
        {
            Set(type, Get(type) + delta);
        }

        /// <summary>
        /// Resistance floor of -1 allows a target to take up to double damage,
        /// and the 0.9 ceiling prevents unkillable enemies.
        /// </summary>
        public static float ClampResistance(float value)
        {
            if (value < -1f)
            {
                return -1f;
            }

            return value > 0.9f ? 0.9f : value;
        }
    }
}
