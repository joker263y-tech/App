using System.Collections.Generic;

namespace Shadowbound.Core.Stats
{
    /// <summary>
    /// Base values plus a modifier stack, resolved on demand.
    ///
    /// Resolution order is fixed and deliberate:
    ///     ((base + flat) * (1 + sumPercentAdditive)) * product(1 + mult_i)
    ///
    /// This means flat bonuses are affected by percentages (a heavier weapon
    /// benefits more from a damage buff), and multiplicative sources compound
    /// with each other rather than piling into one bucket. Results are clamped
    /// at zero and cached until the stack changes, because stats are read
    /// several times per frame per combatant on a mobile budget.
    /// </summary>
    public sealed class StatSet
    {
        private readonly float[] _base;
        private readonly float[] _cache;

        private readonly float[] _flatSum;
        private readonly float[] _additiveSum;
        private readonly float[] _multiplierProduct;

        private readonly List<StatModifier> _modifiers;

        private bool _dirty;

        public StatSet()
        {
            _base = new float[StatIds.Count];
            _cache = new float[StatIds.Count];
            _flatSum = new float[StatIds.Count];
            _additiveSum = new float[StatIds.Count];
            _multiplierProduct = new float[StatIds.Count];
            _modifiers = new List<StatModifier>(16);
            _dirty = true;
        }

        public StatSet(StatId id, float value)
            : this()
        {
            SetBase(id, value);
        }

        /// <summary>Number of live modifiers. Exposed for diagnostics and tests.</summary>
        public int ModifierCount
        {
            get { return _modifiers.Count; }
        }

        public float Base(StatId id)
        {
            return _base[(int)id];
        }

        public void SetBase(StatId id, float value)
        {
            _base[(int)id] = value;
            _dirty = true;
        }

        public void AddToBase(StatId id, float delta)
        {
            _base[(int)id] += delta;
            _dirty = true;
        }

        /// <summary>Resolved value of a stat, including all modifiers.</summary>
        public float Get(StatId id)
        {
            if (_dirty)
            {
                Recompute();
            }

            return _cache[(int)id];
        }

        public void AddModifier(StatModifier modifier)
        {
            if (modifier.Source == null && modifier.Value == 0f)
            {
                return;
            }

            _modifiers.Add(modifier);
            _dirty = true;
        }

        public void AddModifiers(IEnumerable<StatModifier> modifiers)
        {
            if (modifiers == null)
            {
                return;
            }

            foreach (StatModifier modifier in modifiers)
            {
                AddModifier(modifier);
            }
        }

        /// <summary>
        /// Removes every modifier owned by <paramref name="source"/>.
        /// Returns how many were removed. Used when equipment is unequipped or a
        /// status expires.
        /// </summary>
        public int RemoveModifiersFrom(object source)
        {
            if (source == null || _modifiers.Count == 0)
            {
                return 0;
            }

            int removed = 0;
            for (int i = _modifiers.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_modifiers[i].Source, source))
                {
                    _modifiers.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                _dirty = true;
            }

            return removed;
        }

        public void ClearModifiers()
        {
            if (_modifiers.Count == 0)
            {
                return;
            }

            _modifiers.Clear();
            _dirty = true;
        }

        /// <summary>Forces the next <see cref="Get"/> to recompute. Used after bulk base edits.</summary>
        public void Invalidate()
        {
            _dirty = true;
        }

        private void Recompute()
        {
            for (int i = 0; i < StatIds.Count; i++)
            {
                _flatSum[i] = 0f;
                _additiveSum[i] = 0f;
                _multiplierProduct[i] = 1f;
            }

            for (int i = 0; i < _modifiers.Count; i++)
            {
                StatModifier m = _modifiers[i];
                int index = (int)m.Stat;
                if (index < 0 || index >= StatIds.Count)
                {
                    continue;
                }

                switch (m.Op)
                {
                    case ModifierOp.Flat:
                        _flatSum[index] += m.Value;
                        break;
                    case ModifierOp.PercentAdditive:
                        _additiveSum[index] += m.Value;
                        break;
                    case ModifierOp.PercentMultiplicative:
                        _multiplierProduct[index] *= 1f + m.Value;
                        break;
                }
            }

            for (int i = 0; i < StatIds.Count; i++)
            {
                float value = (_base[i] + _flatSum[i]) * (1f + _additiveSum[i]) * _multiplierProduct[i];
                _cache[i] = value < 0f ? 0f : value;
            }

            _dirty = false;
        }
    }
}
