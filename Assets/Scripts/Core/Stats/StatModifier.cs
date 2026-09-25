using System;

namespace Shadowbound.Core.Stats
{
    /// <summary>How a modifier combines with the others applied to the same stat.</summary>
    public enum ModifierOp
    {
        /// <summary>Added before percentages. Equipment flat bonuses.</summary>
        Flat = 0,

        /// <summary>Summed with other additive percentages, then applied once.</summary>
        PercentAdditive = 1,

        /// <summary>Applied as its own multiplier, compounding with other multiplicative sources.</summary>
        PercentMultiplicative = 2
    }

    /// <summary>
    /// A single stat change contributed by one source: a piece of equipment, a
    /// status effect, a level-up, a boss aura.
    ///
    /// Every modifier carries the <see cref="Source"/> that produced it, so a
    /// source can remove exactly its own contributions without disturbing
    /// anything else. Removing a weapon must not silently strip a buff.
    /// </summary>
    public readonly struct StatModifier
    {
        public readonly StatId Stat;
        public readonly ModifierOp Op;
        public readonly float Value;

        /// <summary>The object that owns this modifier: an item, status or system.</summary>
        public readonly object Source;

        public StatModifier(StatId stat, ModifierOp op, float value, object source = null)
        {
            Stat = stat;
            Op = op;
            Value = value;
            Source = source;
        }

        public static StatModifier Flat(StatId stat, float value, object source = null)
        {
            return new StatModifier(stat, ModifierOp.Flat, value, source);
        }

        public static StatModifier Percent(StatId stat, float value, object source = null)
        {
            return new StatModifier(stat, ModifierOp.PercentAdditive, value, source);
        }

        public static StatModifier Multiply(StatId stat, float value, object source = null)
        {
            return new StatModifier(stat, ModifierOp.PercentMultiplicative, value, source);
        }

        /// <summary>
        /// Returns this modifier owned by a different source.
        ///
        /// Authored item definitions hold modifier templates with no source, and
        /// each equipped instance rebinds them to its own token so that
        /// unequipping removes exactly what equipping added.
        /// </summary>
        public StatModifier WithSource(object source)
        {
            return new StatModifier(Stat, Op, Value, source);
        }

        public override string ToString()
        {
            return StatIds.Name(Stat) + " " + Op + " " + Value.ToString("0.###");
        }
    }
}
