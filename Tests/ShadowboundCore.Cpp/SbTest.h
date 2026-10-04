#pragma once

// -----------------------------------------------------------------------------
// A deliberately tiny test framework for the engine-free C++ core.
//
// It exists so the ported rules can be executed with a plain C++ compiler -
// no Unreal Engine, no editor, no third-party dependency - which is the same
// standard the C# suite holds the original core to.
//
// Usage:
//   TEST(MyRule_Whatever) { CHECK(cond); CHECK_EQ(a, b); CHECK_NEAR(a, b, eps); }
// -----------------------------------------------------------------------------

#include <cmath>
#include <cstdio>
#include <string>
#include <vector>

namespace sbtest
{

using TestFn = void (*)();

struct FTestCase
{
	const char* Name;
	TestFn Fn;
};

std::vector<FTestCase>& Registry();
int& CurrentFailures();

struct FRegistrar
{
	FRegistrar(const char* name, TestFn fn);
};

void ReportFailure(const char* file, int line, const std::string& message);

/// Runs every registered test. Returns the number of failed checks.
int RunAll();

} // namespace sbtest

#define TEST(name)                                                     \
	static void name();                                                \
	static sbtest::FRegistrar sbtest_registrar_##name(#name, &name);   \
	static void name()

#define CHECK(cond)                                                    \
	do                                                                 \
	{                                                                  \
		if (!(cond))                                                   \
		{                                                              \
			sbtest::ReportFailure(__FILE__, __LINE__,                  \
				"CHECK failed: " #cond);                               \
		}                                                              \
	} while (false)

#define CHECK_EQ(a, b)                                                 \
	do                                                                 \
	{                                                                  \
		const auto sbtest_a = (a);                                     \
		const auto sbtest_b = (b);                                     \
		if (!(sbtest_a == sbtest_b))                                   \
		{                                                              \
			sbtest::ReportFailure(__FILE__, __LINE__,                  \
				"CHECK_EQ failed: " #a " == " #b);                     \
		}                                                              \
	} while (false)

#define CHECK_NEAR(a, b, tol)                                          \
	do                                                                 \
	{                                                                  \
		const double sbtest_a = static_cast<double>(a);                \
		const double sbtest_b = static_cast<double>(b);                \
		if (std::fabs(sbtest_a - sbtest_b) > static_cast<double>(tol)) \
		{                                                              \
			sbtest::ReportFailure(__FILE__, __LINE__,                  \
				"CHECK_NEAR failed: " #a " ~= " #b);                   \
		}                                                              \
	} while (false)
