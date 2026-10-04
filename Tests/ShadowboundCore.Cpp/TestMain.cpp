#include "SbTest.h"

namespace sbtest
{

std::vector<FTestCase>& Registry()
{
	static std::vector<FTestCase> registry;
	return registry;
}

int& CurrentFailures()
{
	static int failures = 0;
	return failures;
}

FRegistrar::FRegistrar(const char* name, TestFn fn)
{
	Registry().push_back(FTestCase{ name, fn });
}

void ReportFailure(const char* file, int line, const std::string& message)
{
	CurrentFailures()++;
	std::fprintf(stderr, "  %s(%d): %s\n", file, line, message.c_str());
}

int RunAll()
{
	int failedTests = 0;
	int passedTests = 0;

	for (const FTestCase& test : Registry())
	{
		const int before = CurrentFailures();
		test.Fn();

		if (CurrentFailures() > before)
		{
			failedTests++;
			std::fprintf(stderr, "FAILED: %s\n", test.Name);
		}
		else
		{
			passedTests++;
		}
	}

	const int failedChecks = CurrentFailures();

	if (failedTests == 0)
	{
		std::printf("Passed!  - Failed: 0, Passed: %d, Skipped: 0, Total: %d\n",
			passedTests, passedTests);
	}
	else
	{
		std::printf("Failed!  - Failed: %d, Passed: %d, Total: %d (%d failing check(s))\n",
			failedTests, passedTests, failedTests + passedTests, failedChecks);
	}

	return failedTests;
}

} // namespace sbtest

int main()
{
	return sbtest::RunAll() == 0 ? 0 : 1;
}
