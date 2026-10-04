#pragma once

#include <cstdint>
#include <string>

namespace ShadowboundCore
{

// -----------------------------------------------------------------------------
// DeterministicRng
//
// Seeded PCG32 generator, ported from the C# DeterministicRng. Used everywhere
// the game makes a random decision: damage variance, crit rolls, loot, AI
// hesitation.
//
// Two properties matter:
//   1. Determinism. Same seed and same call order produce the same sequence,
//      on every platform and CPU. std::mt19937-style engines differ from
//      standard library to standard library; this one is specified here.
//   2. Reproducibility in tests. A failing combat test can be replayed by
//      asserting on the seed alone.
//
// The algorithm (PCG32 state advance + output function, constants below) is
// byte-for-byte the same arithmetic as the C# original, so the C# test suite
// and the C++ test suite pin the same sequences.
//
// State is publicly readable and restorable so that save games can persist a
// generator mid-sequence.
// -----------------------------------------------------------------------------
class DeterministicRng
{
public:
	/// Rebuilds a generator from previously captured state.
	static DeterministicRng Restore(std::uint64_t state, std::uint64_t increment);

	/// A hash that produces the same value on every process, platform and run
	/// (FNV-1a). Each enemy derives its own generator stream from its id so
	/// that changing one creature's behaviour cannot shift another's - which
	/// only holds if the hash is stable.
	///
	/// Note: the C# original hashed UTF-16 code units; this hashes bytes. For
	/// the ASCII identifiers the content uses, the two are identical.
	static std::uint64_t StableHash(const std::string& text);

	explicit DeterministicRng(std::uint64_t seed);
	DeterministicRng(std::uint64_t seed, std::uint64_t stream);

	/// Current internal state. Persist alongside the increment to resume a sequence.
	std::uint64_t State() const { return _state; }
	std::uint64_t Increment() const { return _increment; }

	/// Advances the sequence and returns 32 random bits.
	std::uint32_t NextUInt();

	/// Uniform float in [0, 1).
	float NextFloat();

	/// Uniform float in [min, max).
	float Range(float min, float max);

	/// Uniform integer in [minInclusive, maxExclusive). Rejection sampling so
	/// every value has exactly equal probability - matters for loot tables.
	int Range(int minInclusive, int maxExclusive);

	/// True with the given probability. Values outside 0..1 are clamped.
	bool Chance(float probability);

	/// A generator whose sequence is independent of this one.
	DeterministicRng Fork(std::uint64_t streamId);

	/// Triangular distribution, useful for damage and loot variance.
	float Triangular(float min, float max, float mode);

private:
	DeterministicRng();

	std::uint64_t _state;
	std::uint64_t _increment;
};

} // namespace ShadowboundCore
