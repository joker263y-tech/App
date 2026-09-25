using System;

namespace Shadowbound.Core.Randomness
{
    /// <summary>
    /// Seeded PCG32 generator. Used everywhere the game makes a random
    /// decision: damage variance, crit rolls, loot, AI hesitation.
    ///
    /// Two properties matter here:
    ///   1. Determinism. Same seed and same call order produce the same
    ///      sequence, on every platform and CPU. System.Random cannot promise
    ///      this, which is why it is not used.
    ///   2. Reproducibility in tests. A failing combat test can be replayed by
    ///      asserting on the seed alone.
    ///
    /// State is publicly readable and restorable so that save games can persist
    /// a generator mid-sequence.
    /// </summary>
    public sealed class DeterministicRng
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong DefaultStream = 1442695040888963407UL;

        private ulong _state;
        private ulong _increment;

        private DeterministicRng()
        {
            _state = 0UL;
            _increment = 1UL;
        }

        public DeterministicRng(ulong seed)
            : this(seed, DefaultStream)
        {
        }

        public DeterministicRng(ulong seed, ulong stream)
        {
            _increment = unchecked((stream << 1) | 1UL);
            _state = 0UL;

            NextUInt();
            _state = unchecked(_state + seed);
            NextUInt();
        }

        /// <summary>Current internal state. Persist alongside the increment to resume a sequence.</summary>
        public ulong State
        {
            get { return _state; }
        }

        public ulong Increment
        {
            get { return _increment; }
        }

        /// <summary>Rebuilds a generator from previously captured state.</summary>
        public static DeterministicRng Restore(ulong state, ulong increment)
        {
            var rng = new DeterministicRng();
            rng._state = state;
            // The increment must stay odd for the generator to have full period.
            rng._increment = increment | 1UL;
            return rng;
        }

        /// <summary>
        /// A hash that produces the same value on every process, platform and run.
        ///
        /// Each enemy derives its own generator stream from its id so that changing
        /// one creature's behaviour cannot shift another's. That only holds if the
        /// hash is stable: string.GetHashCode is deliberately randomised per process
        /// in modern .NET, so using it here would mean the same seed produced
        /// different enemy behaviour on every launch, quietly breaking the
        /// determinism the whole simulation is built on.
        ///
        /// This is FNV-1a, chosen because it is tiny, has no dependencies, and is
        /// specified down to the byte - so it is reproducible anywhere.
        /// </summary>
        public static ulong StableHash(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0UL;
            }

            const ulong OffsetBasis = 14695981039346656037UL;
            const ulong Prime = 1099511628211UL;

            ulong hash = OffsetBasis;

            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash = unchecked(hash * Prime);
            }

            return hash;
        }

        /// <summary>Advances the sequence and returns 32 random bits.</summary>
        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked((old * Multiplier) + _increment);

            uint xorshifted = unchecked((uint)(((old >> 18) ^ old) >> 27));
            int rot = (int)(old >> 59);

            return unchecked((xorshifted >> rot) | (xorshifted << ((-rot) & 31)));
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max)
        {
            if (max <= min)
            {
                return min;
            }

            return min + (NextFloat() * (max - min));
        }

        /// <summary>
        /// Uniform integer in [minInclusive, maxExclusive). Uses rejection
        /// sampling so that every value has exactly equal probability, which
        /// matters for loot distribution over small tables.
        /// </summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            uint span = (uint)(maxExclusive - minInclusive);
            uint threshold = (uint)((1UL << 32) % span);
            uint value;

            do
            {
                value = NextUInt();
            }
            while (value < threshold);

            return minInclusive + (int)(value % span);
        }

        /// <summary>True with the given probability. Values outside 0..1 are clamped.</summary>
        public bool Chance(float probability)
        {
            if (probability <= 0f)
            {
                return false;
            }

            if (probability >= 1f)
            {
                return true;
            }

            return NextFloat() < probability;
        }

        /// <summary>
        /// A generator whose sequence is independent of this one. Used to give
        /// each enemy, loot roll and spawner its own stream so that a change in
        /// one system cannot shift the results of another.
        /// </summary>
        public DeterministicRng Fork(ulong streamId)
        {
            return new DeterministicRng(NextUInt(), streamId);
        }

        /// <summary>Returns a value from a triangular distribution, useful for damage and loot variance.</summary>
        public float Triangular(float min, float max, float mode)
        {
            float u = NextFloat();
            float c = (mode - min) / (max - min);
            if (u < c)
            {
                return min + (float)Math.Sqrt(u * (max - min) * (mode - min));
            }

            return max - (float)Math.Sqrt((1f - u) * (max - min) * (max - mode));
        }
    }
}
