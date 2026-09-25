using System.Collections.Generic;
using Shadowbound.Core.Randomness;
using Xunit;

namespace Shadowbound.Core.Tests.Random
{
    public class DeterministicRngTests
    {
        [Fact]
        public void SameSeed_ProducesTheSameSequence()
        {
            var a = new DeterministicRng(12345);
            var b = new DeterministicRng(12345);

            for (int i = 0; i < 200; i++)
            {
                Assert.Equal(a.NextUInt(), b.NextUInt());
            }
        }

        [Fact]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new DeterministicRng(1);
            var b = new DeterministicRng(2);

            bool diverged = false;
            for (int i = 0; i < 50; i++)
            {
                if (a.NextUInt() != b.NextUInt())
                {
                    diverged = true;
                    break;
                }
            }

            Assert.True(diverged);
        }

        [Fact]
        public void NextFloat_StaysInUnitInterval()
        {
            var rng = new DeterministicRng(777);

            for (int i = 0; i < 10000; i++)
            {
                float value = rng.NextFloat();
                Assert.InRange(value, 0f, 0.9999999f);
            }
        }

        [Fact]
        public void NextFloat_AveragesNearOneHalf()
        {
            var rng = new DeterministicRng(2024);
            double total = 0;

            const int samples = 100000;
            for (int i = 0; i < samples; i++)
            {
                total += rng.NextFloat();
            }

            Assert.InRange(total / samples, 0.49, 0.51);
        }

        [Fact]
        public void Range_Integer_StaysWithinBounds()
        {
            var rng = new DeterministicRng(31337);

            for (int i = 0; i < 5000; i++)
            {
                int value = rng.Range(10, 20);
                Assert.InRange(value, 10, 19);
            }
        }

        [Fact]
        public void Range_Integer_CoversBothEndpoints()
        {
            // A modulo implementation with an off-by-one would miss the top value.
            var rng = new DeterministicRng(5);
            var seen = new HashSet<int>();

            for (int i = 0; i < 5000; i++)
            {
                seen.Add(rng.Range(0, 3));
            }

            Assert.Contains(0, seen);
            Assert.Contains(1, seen);
            Assert.Contains(2, seen);
        }

        [Fact]
        public void Range_Integer_WithEmptySpan_ReturnsTheMinimum()
        {
            var rng = new DeterministicRng(1);

            Assert.Equal(7, rng.Range(7, 7));
            Assert.Equal(7, rng.Range(7, 3));
        }

        [Fact]
        public void Range_Float_StaysWithinBounds()
        {
            var rng = new DeterministicRng(808);

            for (int i = 0; i < 5000; i++)
            {
                Assert.InRange(rng.Range(-5f, 5f), -5f, 5f);
            }
        }

        [Fact]
        public void Range_Float_WithInvertedBounds_ReturnsTheMinimum()
        {
            var rng = new DeterministicRng(1);

            Assert.Equal(2f, rng.Range(2f, 1f));
        }

        [Fact]
        public void Chance_WithZeroOrOne_IsDeterministic()
        {
            var rng = new DeterministicRng(3);

            for (int i = 0; i < 100; i++)
            {
                Assert.False(rng.Chance(0f));
                Assert.True(rng.Chance(1f));
            }
        }

        [Fact]
        public void Chance_ProducesRoughlyTheRequestedProportion()
        {
            var rng = new DeterministicRng(1234);
            int hits = 0;

            for (int i = 0; i < 100000; i++)
            {
                if (rng.Chance(0.3f))
                {
                    hits++;
                }
            }

            Assert.InRange(hits, 29000, 31000);
        }

        [Fact]
        public void Restore_ResumesTheSequenceExactly()
        {
            var original = new DeterministicRng(9090);
            for (int i = 0; i < 10; i++)
            {
                original.NextUInt();
            }

            DeterministicRng resumed = DeterministicRng.Restore(original.State, original.Increment);

            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(original.NextUInt(), resumed.NextUInt());
            }
        }

        [Fact]
        public void Restore_WithEvenIncrement_StillAdvances()
        {
            // An even increment gives the generator a short period, so it is
            // forced odd on restore.
            var rng = DeterministicRng.Restore(42UL, 8UL);

            uint first = rng.NextUInt();
            uint second = rng.NextUInt();

            Assert.NotEqual(first, second);
            Assert.Equal(1UL, rng.Increment & 1UL);
        }

        [Fact]
        public void Fork_IsDeterministicForTheSameParentState()
        {
            var a = new DeterministicRng(555);
            var b = new DeterministicRng(555);

            DeterministicRng forkA = a.Fork(1);
            DeterministicRng forkB = b.Fork(1);

            for (int i = 0; i < 50; i++)
            {
                Assert.Equal(forkA.NextUInt(), forkB.NextUInt());
            }
        }

        [Fact]
        public void Fork_DiffersForDifferentStreams()
        {
            var parent = new DeterministicRng(555);

            DeterministicRng streamA = parent.Fork(1);
            DeterministicRng streamB = parent.Fork(2);

            Assert.NotEqual(streamA.NextUInt(), streamB.NextUInt());
        }

        [Fact]
        public void Fork_DoesNotDisturbTheParentIdenticallyToNotForking()
        {
            // Two parents at the same point must continue to agree after forks,
            // so that adding a new forked system cannot desync an existing one.
            var a = new DeterministicRng(3131);
            var b = new DeterministicRng(3131);

            a.Fork(1);
            b.Fork(1);

            Assert.Equal(a.NextUInt(), b.NextUInt());
        }

        [Fact]
        public void Triangular_StaysWithinBoundsAndFavoursTheMode()
        {
            var rng = new DeterministicRng(606);
            double total = 0;

            const int samples = 20000;
            for (int i = 0; i < samples; i++)
            {
                float value = rng.Triangular(0f, 10f, 5f);
                Assert.InRange(value, 0f, 10f);
                total += value;
            }

            // A symmetric triangular distribution has mean == mode.
            Assert.InRange(total / samples, 4.8, 5.2);
        }
    }
}
