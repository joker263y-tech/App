using UnrealBuildTool;

public class ShadowboundCore : ModuleRules
{
	public ShadowboundCore(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.NoPCHs;
		CppStandard = CppStandardVersion.Cpp17;

		// Engine-free module. Every game rule in this module compiles against the
		// C++ standard library alone - no CoreMinimal.h, no engine headers - which
		// is what lets the same sources compile and run their tests with a plain
		// C++ compiler in Tools/test-core-cpp.sh, without Unreal Engine installed.
		//
		// "Core" is depended on only for module registration (IMPLEMENT_MODULE in
		// ShadowboundCoreModule.cpp); no other file in this module may include an
		// engine header. Tools/check-core-purity.sh enforces that.
		PrivateDependencyModuleNames.AddRange(new string[]
		{
			"Core"
		});
	}
}
