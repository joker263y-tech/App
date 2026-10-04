#include "SbRng.h"

#include <cmath>

namespace ShadowboundCore
{

namespace
{
constexpr std::uint64_t Multiplier = 6364136223846793005ULL;
constexpr std::uint64_t DefaultStream = 1442695040888963407ULL;
} // namespace

DeterministicRng::DeterministicRng()
	: _state(0ULL)
	, _increment(1ULL)
{
}

DeterministicRng::DeterministicRng(std::uint64_t seed)
	: DeterministicRng(seed, DefaultStream)
{
}

DeterministicRng::DeterministicRng(std::uint64_t seed, std::uint64_t stream)
	: _state(0ULL)
	, _increment((stream << 1) | 1ULL)
{
	NextUInt();
	_state += seed;
	NextUInt();
}

DeterministicRng DeterministicRng::Restore(std::uint64_t state, std::uint64_t increment)
{
	DeterministicRng rng;
	rng._state = state;
	// The increment must stay odd for the generator to have full period.
	rng._increment = increment | 1ULL;
	return rng;
}

std::uint64_t DeterministicRng::StableHash(const std::string& text)
{
	if (text.empty())
	{
		return 0ULL;
	}

	constexpr std::uint64_t OffsetBasis = 14695981039346656037ULL;
	constexpr std::uint64_t Prime = 1099511628211ULL;

	std::uint64_t hash = OffsetBasis;

	for (const char character : text)
	{
		hash ^= static_cast<std::uint64_t>(static_cast<unsigned char>(character));
		hash *= Prime;
	}

	return hash;
}

std::uint32_t DeterministicRng::NextUInt()
{
	const std::uint64_t old = _state;
	_state = (old * Multiplier) + _increment;

	const std::uint32_t xorshifted = static_cast<std::uint32_t>(((old >> 18) ^ old) >> 27);
	const int rot = static_cast<int>(old >> 59);

	return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
}

float DeterministicRng::NextFloat()
{
	return static_cast<float>(NextUInt() >> 8) * (1.0f / 16777216.0f);
}

float DeterministicRng::Range(float min, float max)
{
	if (max <= min)
	{
		return min;
	}

	return min + (NextFloat() * (max - min));
}

int DeterministicRng::Range(int minInclusive, int maxExclusive)
{
	if (maxExclusive <= minInclusive)
	{
		return minInclusive;
	}

	const std::uint32_t span = static_cast<std::uint32_t>(maxExclusive - minInclusive);
	const std::uint32_t threshold = static_cast<std::uint32_t>(4294967296ULL % span);

	std::uint32_t value;
	do
	{
		value = NextUInt();
	} while (value < threshold);

	return minInclusive + static_cast<int>(value % span);
}

bool DeterministicRng::Chance(float probability)
{
	if (probability <= 0.f)
	{
		return false;
	}

	if (probability >= 1.f)
	{
		return true;
	}

	return NextFloat() < probability;
}

DeterministicRng DeterministicRng::Fork(std::uint64_t streamId)
{
	return DeterministicRng(NextUInt(), streamId);
}

float DeterministicRng::Triangular(float min, float max, float mode)
{
	const float u = NextFloat();
	const float c = (mode - min) / (max - min);
	if (u < c)
	{
		return min + std::sqrt(u * (max - min) * (mode - min));
	}

	return max - std::sqrt((1.f - u) * (max - min) * (max - mode));
}

} // namespace ShadowboundCore
