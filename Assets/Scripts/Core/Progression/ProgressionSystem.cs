using System;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Stats;

namespace Shadowbound.Core.Progression
{
    /// <summary>How much a stat grows per level for a given archetype.</summary>
    public readonly struct StatGrowth
    {
        public readonly StatId Stat;
        public readonly float PerLevel;

        public StatGrowth(StatId stat, float perLevel)
        {
            Stat = stat;
            PerLevel = perLevel < 0f ? 0f : perLevel;
        }
    }

    /// <summary>
    /// Experience, levels and the stat growth that comes with them.
    ///
    /// Growth is applied to base stats rather than as modifiers, because a level
    /// is permanent and should not be removable the way equipment is. Modifiers
    /// are reserved for things that can come off again.
    ///
    /// On level up, any increase to the health ceiling is granted as healing.
    /// Without that, gaining a level would raise the maximum while leaving the
    /// current value untouched, so the player would appear to lose health
    /// proportionally at the moment they were rewarded.
    /// </summary>
    public sealed class ProgressionSystem
    {
        private readonly Combatant _owner;
        private readonly ExperienceCurve _curve;
        private readonly StatGrowth[] _growth;

        private int _totalExperience;

        public ProgressionSystem(Combatant owner, ExperienceCurve curve, StatGrowth[] growth)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _curve = curve ?? new ExperienceCurve();
            _growth = growth ?? Array.Empty<StatGrowth>();

            _totalExperience = 0;
            Level = 1;
            _owner.Level = 1;
        }

        /// <summary>Raised once per level gained, with the new level.</summary>
        public event Action<int> LeveledUp;

        public int Level { get; private set; }

        /// <summary>Lifetime experience. Never decreases.</summary>
        public int TotalExperience
        {
            get { return _totalExperience; }
        }

        public ExperienceCurve Curve
        {
            get { return _curve; }
        }

        /// <summary>Points available to spend on attributes.</summary>
        public int UnspentAttributePoints { get; private set; }

        public bool IsAtLevelCap
        {
            get { return Level >= _curve.LevelCap; }
        }

        /// <summary>Experience accumulated inside the current level.</summary>
        public int ExperienceIntoLevel
        {
            get { return _totalExperience - _curve.TotalExperienceAtLevel(Level); }
        }

        /// <summary>Experience needed to reach the next level, or 0 at the cap.</summary>
        public int ExperienceForNextLevel
        {
            get { return _curve.ExperienceForNextLevel(Level); }
        }

        /// <summary>Progress through the current level, 0..1. Reports 1 at the cap.</summary>
        public float LevelProgress
        {
            get
            {
                if (IsAtLevelCap)
                {
                    return 1f;
                }

                int needed = ExperienceForNextLevel;
                if (needed <= 0)
                {
                    return 1f;
                }

                return FMath.Clamp01(ExperienceIntoLevel / (float)needed);
            }
        }

        /// <summary>
        /// Awards experience and applies any levels gained. Returns how many
        /// levels were gained, so the caller can trigger the level-up
        /// presentation exactly that many times.
        /// </summary>
        public int AddExperience(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            _totalExperience += amount;

            int targetLevel = _curve.LevelForExperience(_totalExperience);
            int gained = 0;

            while (Level < targetLevel)
            {
                Level++;
                ApplyLevelGrowth();
                UnspentAttributePoints++;
                gained++;

                _owner.Level = Level;
                LeveledUp?.Invoke(Level);
            }

            return gained;
        }

        /// <summary>
        /// Spends one attribute point on a stat. Returns false when no points are
        /// available, so the caller can distinguish a refused spend from a
        /// successful one worth confirming on screen.
        /// </summary>
        public bool TrySpendAttributePoint(StatId stat, float amount)
        {
            if (UnspentAttributePoints <= 0 || amount <= 0f)
            {
                return false;
            }

            UnspentAttributePoints--;
            _owner.Stats.AddToBase(stat, amount);
            return true;
        }

        public void GrantAttributePoints(int amount)
        {
            if (amount > 0)
            {
                UnspentAttributePoints += amount;
            }
        }

        /// <summary>
        /// Restores saved progression. Growth is reapplied from scratch for the
        /// saved level rather than trusted from the save, so a change to the
        /// growth table applies to existing characters instead of leaving them
        /// permanently built on an old table.
        /// </summary>
        public void LoadFrom(int totalExperience, int unspentPoints)
        {
            _totalExperience = totalExperience < 0 ? 0 : totalExperience;
            Level = _curve.LevelForExperience(_totalExperience);

            for (int i = 0; i < _growth.Length; i++)
            {
                StatGrowth growth = _growth[i];
                if (growth.PerLevel > 0f && Level > 1)
                {
                    _owner.Stats.AddToBase(growth.Stat, growth.PerLevel * (Level - 1));
                }
            }

            UnspentAttributePoints = unspentPoints < 0 ? 0 : unspentPoints;
            _owner.Level = Level;
        }

        private void ApplyLevelGrowth()
        {
            if (_growth.Length == 0)
            {
                return;
            }

            float healthBefore = _owner.Vitals.MaxHealth;

            for (int i = 0; i < _growth.Length; i++)
            {
                StatGrowth growth = _growth[i];
                if (growth.PerLevel > 0f)
                {
                    _owner.Stats.AddToBase(growth.Stat, growth.PerLevel);
                }
            }

            float healthGained = _owner.Vitals.MaxHealth - healthBefore;
            if (healthGained > 0f)
            {
                _owner.Vitals.Heal(healthGained);
            }
        }
    }
}
