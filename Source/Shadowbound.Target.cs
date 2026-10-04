using UnrealBuildTool;
using System.Collections.Generic;

public class ShadowboundTarget : TargetRules
{
	public ShadowboundTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.Latest;

		ExtraModuleNames.AddRange(new string[]
		{
			"Shadowbound",
			"ShadowboundCore"
		});
	}
}
