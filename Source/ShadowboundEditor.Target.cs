using UnrealBuildTool;
using System.Collections.Generic;

public class ShadowboundEditorTarget : TargetRules
{
	public ShadowboundEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.Latest;

		ExtraModuleNames.AddRange(new string[]
		{
			"Shadowbound",
			"ShadowboundCore"
		});
	}
}
