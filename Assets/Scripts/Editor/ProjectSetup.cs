using System;
using System.IO;
using Shadowbound.Core.Content;
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

        /// <summary>
        /// Runs the core's content validator and reports the result.
        ///
        /// The checking itself lives in Shadowbound.Core.ContentValidator rather
        /// than here, which matters for two reasons: it is ordinary data logic with
        /// no engine dependency, and keeping it in the core means it is covered by
        /// the test suite - so the validator is proven to catch the mistakes it
        /// claims to, and not merely to return a clean bill of health.
        ///
        /// This method is only the reporting.
        /// </summary>
        [MenuItem("Shadowbound/Validate Content", false, 22)]
        public static void ValidateContent()
        {
            ContentReport report;

            try
            {
                report = ContentValidator.ValidateShippedContent();
            }
            catch (Exception exception)
            {
                // Content that cannot even be constructed is the most serious case,
                // so it is reported rather than allowed to surface as a stack trace.
                Debug.LogError("Shadowbound: content failed to build: " + exception);
                return;
            }

            for (int i = 0; i < report.Problems.Count; i++)
            {
                ContentProblem problem = report.Problems[i];
                string line = "Shadowbound content: " + problem;

                if (problem.Severity == ContentProblemSeverity.Error)
                {
                    Debug.LogError(line);
                }
                else
                {
                    Debug.LogWarning(line);
                }
            }

            if (report.IsClean)
            {
                Debug.Log("Shadowbound content: " + report.Summary());
            }
            else
            {
                Debug.LogError("Shadowbound content: " + report.Summary());
            }
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

            try
            {
                BuildAndroidApkTo(Path.Combine(outputDirectory, "Shadowbound.apk"));
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Shadowbound: the build failed. This usually means the Android module " +
                    "(with SDK, NDK and JDK) is not installed for this Unity version.\n" + exception);
            }
        }

        /// <summary>
        /// Entry point for command-line and CI builds. game-ci's unity-builder
        /// calls this via -executeMethod.
        ///
        /// Two differences from the menu version matter here. The output goes
        /// where the CI action collects artifacts from rather than to Builds/.
        /// And a content problem THROWS instead of being logged: a CI job that
        /// logs an error and still exits successfully ships a broken APK with a
        /// green tick next to it, which is worse than no CI at all.
        /// </summary>
        public static void BuildAndroidFromCommandLine()
        {
            CreatePlayableScene();
            ConfigureForAndroid();

            int contentErrors = CountContentErrors();

            if (contentErrors > 0)
            {
                throw new BuildFailedException(
                    "Shadowbound: " + contentErrors + " content error(s); refusing to build.");
            }

            string outputPath = Path.Combine("build", "Android", "Shadowbound.apk");
            string outputDirectory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            BuildAndroidApkTo(outputPath);
        }

        private static int CountContentErrors()
        {
            try
            {
                ContentReport report = ContentValidator.ValidateShippedContent();
                int errors = 0;

                for (int i = 0; i < report.Problems.Count; i++)
                {
                    if (report.Problems[i].Severity == ContentProblemSeverity.Error)
                    {
                        errors++;
                    }
                }

                return errors;
            }
            catch (Exception exception)
            {
                // Content that cannot even be constructed is itself a build blocker.
                Debug.LogError("Shadowbound: content failed to build: " + exception);
                return 1;
            }
        }

        /// <summary>The build itself. Throws when the build fails, for any caller that must not continue.</summary>
        private static void BuildAndroidApkTo(string outputPath)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "Shadowbound: build " + summary.result + " with " +
                    summary.totalErrors + " error(s).");
            }

            Debug.Log(
                "Shadowbound: APK built at " + outputPath +
                " (" + (summary.totalSize / (1024 * 1024)) + " MB in " + summary.totalTime + ").");
        }

        [MenuItem("Shadowbound/Log Save Location", false, 60)]
        public static void LogSaveLocation()
        {
            Debug.Log("Shadowbound save directory: " + Path.Combine(Application.persistentDataPath, "saves"));
        }
    }
}
