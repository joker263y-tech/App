using System;

namespace Shadowbound.Core.Progression
{
    /// <summary>
    /// Maps experience to levels.
    ///
    /// The curve is a power function: the experience needed to leave level L is
    /// Base * L^Exponent. A power curve is used rather than a linear one so that
    /// early levels arrive quickly, which is what makes the opening of the game
    /// feel responsive, while later levels stay meaningful without the numbers
    /// exploding into the millions.
    ///
    /// The curve is the single source of truth for levelling. Converting in both
    /// directions from here is what stops the HUD, the progression system and the
    /// save file disagreeing about how far along the player is.
    /// </summary>
    public sealed class ExperienceCurve
    {
        /// <summary>Highest level reachable. Further experience is discarded.</summary>
        public int LevelCap = 30;

        /// <summary>Experience required to go from level 1 to level 2.</summary>
        public float BaseExperience = 120f;

        /// <summary>Growth exponent. Values above 1 make each level take longer than the last.</summary>
        public float Exponent = 1.55f;

        public ExperienceCurve()
        {
        }

        public ExperienceCurve(int levelCap, float baseExperience, float exponent)
        {
            LevelCap = levelCap < 1 ? 1 : levelCap;
            BaseExperience = baseExperience <= 0f ? 1f : baseExperience;
            Exponent = exponent <= 0f ? 1f : exponent;
        }

        /// <summary>Experience needed to advance from <paramref name="level"/> to the next one.</summary>
        public int ExperienceForNextLevel(int level)
        {
            if (level < 1)
            {
                level = 1;
            }

            if (level >= LevelCap)
            {
                return 0;
            }

            return (int)Math.Round(BaseExperience * Math.Pow(level, Exponent));
        }

        /// <summary>Total accumulated experience required to have reached <paramref name="level"/>.</summary>
        public int TotalExperienceAtLevel(int level)
        {
            if (level <= 1)
            {
                return 0;
            }

            int capped = level > LevelCap ? LevelCap : level;
            int total = 0;

            for (int current = 1; current < capped; current++)
            {
                total += ExperienceForNextLevel(current);
            }

            return total;
        }

        /// <summary>
        /// Level reached by accumulating <paramref name="totalExperience"/>.
        /// Never exceeds the cap, so a save edited to a huge value cannot break
        /// the game.
        /// </summary>
        public int LevelForExperience(int totalExperience)
        {
            if (totalExperience <= 0)
            {
                return 1;
            }

            int level = 1;
            int remaining = totalExperience;

            while (level < LevelCap)
            {
                int needed = ExperienceForNextLevel(level);
                if (needed <= 0 || remaining < needed)
                {
                    break;
                }

                remaining -= needed;
                level++;
            }

            return level;
        }
    }
}
