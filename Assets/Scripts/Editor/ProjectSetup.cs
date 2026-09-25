using System;
using System.Collections.Generic;
using System.IO;
using Shadowbound.Core.Content;
using Shadowbound.Core.Items;
using Shadowbound.Core.Quests;
using Shadowbound.Core.Serialization;
using Shadowbound.Core.World;
using Shadowbound.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Shadowbound.Editor
{
    /// <summary>
    /// One-click project setup.
    ///
    /// The game is assembled at runtime by <see cref="GameBootstrap"/>, so the scene
    /// on disk is nearly empty and there are no prefabs to keep in sync. That means
    /// the setup work a person would otherwise do by hand - creating the scene,
    /// pointing the build settings at it, and configuring the Android player - is
    /// expressed here as code instead of as instructions to follow carefully.
    ///
    /// Menu: Shadowbound.
    /// </summary>
    public static class ProjectSetup
    {
        private const string SceneDirectory = "Assets/Scenes";
        private const string ScenePath = SceneDirectory + "/Arena.unity";

        private const string CompanyName = "Shadowbound";
        private const string ProductName = "Shadowbound: The Last Night";
        private const string ApplicationId = "com.shadowbound.thelastnight";

        // [MenuItem] methods must be static and take no arguments, so each entry
        // point below is a thin wrapper that reports success or failure to the user.

        [MenuItem("Shadowbound/Set Up Project", false, 0)]
        public static void SetUpProject()
        {
            CreatePlayableScene();
            ConfigureForAndroid();
            ValidateContent();

            EditorUtility.DisplayDialog(
                "Shadowbound",
                "Project is set up.\n\n" +
                "Scene: " + ScenePath + "\n" +
                "Android build target configured.\n\n" +
                "Press Play to run, or use Shadowbound > Build Android APK.",
                "Close");
        }

        [MenuItem("Shadowbound/Create Playable Scene", false, 20)]
        public static void CreatePlayableScene()
        {
            if (!Directory.Exists(SceneDirectory))
            {
                Directory.CreateDirectory(SceneDirectory);
            }

            // A fresh empty scene, so re-running this after a change never leaves a
            // second copy of the game object behind.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var host = new GameObject("Game");
            host.AddComponent<GameBootstrap>();

            EditorSceneManager.MarkSceneDirty(scene);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("Shadowbound: could not save the scene to " + ScenePath);
                return;
            }

            AssetDatabase.Refresh();

            // The scene must be the one the build launches, or an APK would start
            // to an empty screen.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            Debug.Log("Shadowbound: created " + ScenePath + " and set it as the build scene.");
        }

        [MenuItem("Shadowbound/Configure for Android", false, 21)]
        public static void ConfigureForAndroid()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);

            // A third-person action game is unplayable in portrait.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // Linear lighting, because the game is dark and banding in the shadows is
            // the first thing anyone notices.
            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // IL2CPP with ARM64 only. ARM64 is required by Play Store submissions,
            // and Mono would not be acceptable for performance on the target devices.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Vulkan first with an OpenGL ES fallback: Vulkan is measurably better on
            // the target hardware, but a driver bug should demote rather than crash.
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            // Developer builds strip nothing and are twice the size.
            PlayerSettings.Android.useCustomKeystore = false;

            Debug.Log("Shadowbound: Android player settings configured (IL2CPP, ARM64, Vulkan + GLES3, landscape).");
        }

        [MenuItem("Shadowbound/Validate Content", false, 22)]
        public static void ValidateContent()
        {
            int problems = 0;

            try
            {
                ItemDatabase items = GameContent.BuildItems();
                List<QuestDefinition> quests = GameContent.BuildQuests();
                List<ChapterDefinition> chapters = GameContent.BuildChapters();
                List<RegionDefinition> regions = GameContent.BuildRegions();
                Dictionary<string, LootTable> loot = GameContent.BuildLootTables();
                List<EnemyArchetype> archetypes = GameContent.BuildEnemyArchetypes();

                // Cross-reference checks. Each of these catches an authoring mistake
                // that would otherwise only show up as a silent no-op at runtime:
                // a loot entry naming a missing item, or a quest naming a creature
                // that is never spawned.
                foreach (KeyValuePair<string, LootTable> entry in loot)
                {
                    problems += CheckLootTable(entry.Value, items);
                }

                for (int i = 0; i < archetypes.Count; i++)
                {
                    EnemyArchetype archetype = archetypes[i];

                    if (archetype.Abilities == null || archetype.Abilities.Count == 0)
                    {
                        Debug.LogWarning("Shadowbound: archetype '" + archetype.Id + "' has no abilities.");
                        problems++;
                    }
                    else if (archetype.AttackAbilityIndex < 0 ||
                             archetype.AttackAbilityIndex >= archetype.Abilities.Count)
                    {
                        Debug.LogError(
                            "Shadowbound: archetype '" + archetype.Id + "' attacks with ability index " +
                            archetype.AttackAbilityIndex + " but only has " + archetype.Abilities.Count + ".");
                        problems++;
                    }

                    if (!string.IsNullOrEmpty(archetype.LootTableId) && !loot.ContainsKey(archetype.LootTableId))
                    {
                        Debug.LogError(
                            "Shadowbound: archetype '" + archetype.Id + "' points at loot table '" +
                            archetype.LootTableId + "', which does not exist.");
                        problems++;
                    }
                }

                Debug.Log(
                    "Shadowbound content: " + items.Count + " items, " + archetypes.Count + " archetypes, " +
                    loot.Count + " loot tables, " + quests.Count + " quests, " + chapters.Count +
                    " chapters, " + regions.Count + " regions. " +
                    (problems == 0 ? "No problems found." : problems + " problem(s) found."));
            }
            catch (Exception exception)
            {
                Debug.LogError("Shadowbound: content failed to build: " + exception);
                problems++;
            }

            if (problems > 0)
            {
                Debug.LogWarning("Shadowbound: content validation finished with " + problems + " problem(s).");
            }
        }

        private static int CheckLootTable(LootTable table, ItemDatabase items)
        {
            int problems = 0;

            problems += CheckLootEntries(table, table.Guaranteed, "guaranteed", items);
            problems += CheckLootEntries(table, table.Weighted, "weighted", items);

            if (table.Guaranteed.Length == 0 && table.Weighted.Length == 0)
            {
                Debug.LogWarning("Shadowbound: loot table '" + table.Id + "' cannot drop anything.");
                problems++;
            }

            return problems;
        }

        private static int CheckLootEntries(
            LootTable table,
            LootEntry[] entries,
            string group,
            ItemDatabase items)
        {
            int problems = 0;

            for (int i = 0; i < entries.Length; i++)
            {
                LootEntry entry = entries[i];

                if (string.IsNullOrEmpty(entry.ItemId))
                {
                    Debug.LogWarning(
                        "Shadowbound: loot table '" + table.Id + "' has a " + group + " entry with no item id.");
                    problems++;
                    continue;
                }

                if (!items.TryGet(entry.ItemId, out _))
                {
                    Debug.LogError(
                        "Shadowbound: loot table '" + table.Id + "' can drop '" + entry.ItemId +
                        "', which is not a registered item. The drop would silently yield nothing.");
                    problems++;
                }

                if (entry.MinQuantity < 1 || entry.MaxQuantity < entry.MinQuantity)
                {
                    Debug.LogError(
                        "Shadowbound: loot table '" + table.Id + "' has an invalid quantity range for '" +
                        entry.ItemId + "' (" + entry.MinQuantity + ".." + entry.MaxQuantity + ").");
                    problems++;
                }
            }

            return problems;
        }

        [MenuItem("Shadowbound/Build Android APK", false, 40)]
        public static void BuildAndroidApk()
        {
            if (!File.Exists(ScenePath))
            {
                CreatePlayableScene();
            }

            string outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
            Directory.CreateDirectory(outputDirectory);

            string outputPath = Path.Combine(outputDirectory, "Shadowbound.apk");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            try
            {
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log(
                        "Shadowbound: APK built at " + outputPath +
                        " (" + (summary.totalSize / (1024 * 1024)) + " MB in " + summary.totalTime + ").");
                }
                else
                {
                    Debug.LogError("Shadowbound: build " + summary.result + " with " + summary.totalErrors + " error(s).");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Shadowbound: the build failed. This usually means the Android module " +
                    "(with SDK, NDK and JDK) is not installed for this Unity version.\n" + exception);
            }
        }

        [MenuItem("Shadowbound/Log Save Location", false, 60)]
        public static void LogSaveLocation()
        {
            Debug.Log("Shadowbound save directory: " + Path.Combine(Application.persistentDataPath, "saves"));
        }
    }
}
