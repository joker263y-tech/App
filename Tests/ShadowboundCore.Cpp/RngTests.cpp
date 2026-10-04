#include "SbTest.h"

#include "SbRng.h"

using ShadowboundCore::DeterministicRng;

TEST(Rng_SameSeedSameSequence)
{
	DeterministicRng a(20250925ULL);
	DeterministicRng b(20250925ULL);

	for (int i = 0; i < 100; i++)
	{
		CHECK_EQ(a.NextUInt(), b.NextUInt());
	}
}

TEST(Rng_DifferentSeedsDiverge)
{
	DeterministicRng a(1ULL);
	DeterministicRng b(2ULL);

	CHECK(a.NextUInt() != b.NextUInt());
}

TEST(Rng_NextFloatStaysInUnitRange)
{
	DeterministicRng rng(7ULL);
	for (int i = 0; i < 1000; i++)
	{
		const float value = rng.NextFloat();
		CHECK(value >= 0.f);
		CHECK(value < 1.f);
	}
}

TEST(Rng_IntRangeIsBoundedAndHandlesDegenerateCases)
{
	DeterministicRng rng(99ULL);

	CHECK_EQ(rng.Range(5, 5), 5);
	CHECK_EQ(rng.Range(9, 4), 9);

	for (int i = 0; i < 500; i++)
	{
		const int value = rng.Range(3, 7);
		CHECK(value >= 3);
		CHECK(value < 7);
	}
}

TEST(Rng_ChanceHonoursExtremes)
{
	DeterministicRng rng(5ULL);
	for (int i = 0; i < 20; i++)
	{
		CHECK(!rng.Chance(0.f));
		CHECK(rng.Chance(1.f));
	}
}

TEST(Rng_StableHashIsFnv1a)
{
	// FNV-1a 64 of "a" is a published constant of the algorithm.
	CHECK_EQ(DeterministicRng::StableHash("a"), 0xaf63dc4c8601ec8cULL);
	// Empty input yields 0 in the C# original.
	CHECK_EQ(DeterministicRng::StableHash(""), 0ULL);

	// Stability across runs and separation between ids: each enemy forks its
	// own stream from this hash, so identical ids would make two creatures
	// behave identically.
	CHECK(DeterministicRng::StableHash("hollow-walker-0")
		!= DeterministicRng::StableHash("hollow-walker-1"));
}

TEST(Rng_RestoreResumesTheSequence)
{
	DeterministicRng original(424242ULL);
	for (int i = 0; i < 10; i++)
	{
		original.NextUInt();
	}

	const std::uint64_t state = original.State();
	const std::uint64_t increment = original.Increment();

	const std::uint32_t expected[5] = {
		original.NextUInt(), original.NextUInt(), original.NextUInt(),
		original.NextUInt(), original.NextUInt()
	};

	DeterministicRng restored = DeterministicRng::Restore(state, increment);
	for (int i = 0; i < 5; i++)
	{
		CHECK_EQ(restored.NextUInt(), expected[i]);
	}
}

TEST(Rng_ForksAreIndependent)
{
	DeterministicRng parent(1234ULL);
	DeterministicRng forkA = parent.Fork(1ULL);
	DeterministicRng forkB = parent.Fork(2ULL);

	CHECK(forkA.NextUInt() != forkB.NextUInt());
}

TEST(Rng_TriangularStaysInRange)
{
	DeterministicRng rng(8ULL);
	for (int i = 0; i < 500; i++)
	{
		const float value = rng.Triangular(10.f, 20.f, 12.f);
		CHECK(value >= 10.f);
		CHECK(value <= 20.f);
	}
}
