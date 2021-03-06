using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
#if !UNITY_ANDROID
using UnityEditor.iOS.Xcode;
#endif
using UnityEngine;

namespace UnityBuilderAction
{
    public static class BuildScript
    {
        private static readonly string Eol = Environment.NewLine;

        private static readonly string[] Secrets =
            {"androidKeystorePass", "androidKeyaliasName", "androidKeyaliasPass"};

        [MenuItem("Build/Build")]
        public static void Build()
        {
            // Gather values from args
            Dictionary<string, string> options = GetValidatedOptions();

            // Set version for this build
            PlayerSettings.bundleVersion = options["buildVersion"];
            PlayerSettings.macOS.buildNumber = options["buildVersion"];
            PlayerSettings.Android.bundleVersionCode = int.Parse(options["androidVersionCode"]);

            // Apply build target
            var buildTarget = (BuildTarget) Enum.Parse(typeof(BuildTarget), options["buildTarget"]);
            switch (buildTarget)
            {
                case BuildTarget.Android:
                {
                    EditorUserBuildSettings.buildAppBundle = options["customBuildPath"].EndsWith(".aab");
                    if (options.TryGetValue("androidKeystoreName", out string keystoreName) &&
                        !string.IsNullOrEmpty(keystoreName))
                        PlayerSettings.Android.keystoreName = keystoreName;
                    if (options.TryGetValue("androidKeystorePass", out string keystorePass) &&
                        !string.IsNullOrEmpty(keystorePass))
                        PlayerSettings.Android.keystorePass = keystorePass;
                    if (options.TryGetValue("androidKeyaliasName", out string keyaliasName) &&
                        !string.IsNullOrEmpty(keyaliasName))
                        PlayerSettings.Android.keyaliasName = keyaliasName;
                    if (options.TryGetValue("androidKeyaliasPass", out string keyaliasPass) &&
                        !string.IsNullOrEmpty(keyaliasPass))
                        PlayerSettings.Android.keyaliasPass = keyaliasPass;
                    break;
                }
                case BuildTarget.StandaloneOSX:
                    PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
                    break;
            }

            // Custom build
            Build(buildTarget, options["customBuildPath"]);
        }

        private static Dictionary<string, string> GetValidatedOptions()
        {
            ParseCommandLineArguments(out Dictionary<string, string> validatedOptions);

            if (!validatedOptions.TryGetValue("projectPath", out string _))
            {
                Console.WriteLine("Missing argument -projectPath");
                EditorApplication.Exit(110);
            }

            if (!validatedOptions.TryGetValue("buildTarget", out string buildTarget))
            {
                Console.WriteLine("Missing argument -buildTarget");
                EditorApplication.Exit(120);
            }

            if (!Enum.IsDefined(typeof(BuildTarget), buildTarget ?? string.Empty))
            {
                EditorApplication.Exit(121);
            }

            if (!validatedOptions.TryGetValue("customBuildPath", out string _))
            {
                Console.WriteLine("Missing argument -customBuildPath");
                EditorApplication.Exit(130);
            }

            const string defaultCustomBuildName = "TestBuild";
            if (!validatedOptions.TryGetValue("customBuildName", out string customBuildName))
            {
                Console.WriteLine($"Missing argument -customBuildName, defaulting to {defaultCustomBuildName}.");
                validatedOptions.Add("customBuildName", defaultCustomBuildName);
            }
            else if (customBuildName == "")
            {
                Console.WriteLine($"Invalid argument -customBuildName, defaulting to {defaultCustomBuildName}.");
                validatedOptions.Add("customBuildName", defaultCustomBuildName);
            }

            return validatedOptions;
        }

        private static void ParseCommandLineArguments(out Dictionary<string, string> providedArguments)
        {
            providedArguments = new Dictionary<string, string>();
            string[] args = Environment.GetCommandLineArgs();

            Console.WriteLine(
                $"{Eol}" +
                $"###########################{Eol}" +
                $"#    Parsing settings     #{Eol}" +
                $"###########################{Eol}" +
                $"{Eol}"
            );

            // Extract flags with optional values
            for (int current = 0, next = 1; current < args.Length; current++, next++)
            {
                // Parse flag
                bool isFlag = args[current].StartsWith("-");
                if (!isFlag) continue;
                string flag = args[current].TrimStart('-');

                // Parse optional value
                bool flagHasValue = next < args.Length && !args[next].StartsWith("-");
                string value = flagHasValue ? args[next].TrimStart('-') : "";
                bool secret = Secrets.Contains(flag);
                string displayValue = secret ? "*HIDDEN*" : "\"" + value + "\"";

                // Assign
                Console.WriteLine($"Found flag \"{flag}\" with value {displayValue}.");

                if (providedArguments.ContainsKey(flag))
                {
                    providedArguments[flag] = value;
                }
                else
                {
                    providedArguments.Add(flag, value);
                }
            }
        }

        private static void Build(BuildTarget buildTarget, string filePath)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(s => s.path).ToArray();
            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                target = buildTarget,
//                targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget),
                locationPathName = filePath,
options = buildTarget == BuildTarget.Android ? BuildOptions.AcceptExternalModificationsToPlayer |
          BuildOptions.Development |
          BuildOptions.AllowDebugging : BuildOptions.None
            };

            if (buildTarget == BuildTarget.Android)
            {
                EditorUserBuildSettings.exportAsGoogleAndroidProject = true;

                if (!Directory.Exists(filePath))
                {
                    Directory.CreateDirectory(filePath);
                }
            }

            BuildSummary buildSummary = BuildPipeline.BuildPlayer(buildPlayerOptions).summary;

            if (buildTarget == BuildTarget.Android)
            {
                var tempDirectoryPath = filePath.Replace("/build/Android/Android.apk", "/temp");
                Debug.Log("Orig Path: " + filePath + " || temp Path: " + tempDirectoryPath);
                
                
                Directory.Move(filePath, tempDirectoryPath);

                if (Directory.Exists(filePath.Replace("/build/Android/Android.apk", "/build")))
                {
                    Debug.Log("Deleting build folder: " + filePath.Replace("/build/Android/Android.apk", "/build"));
                    Directory.Delete(filePath.Replace("/build/Android/Android.apk", "/build"), true);
                    //DeleteDirectory(filePath.Replace("/build/Android/Android.apk", "/build"));
                    Debug.Log("Does build folder exist? : " + Directory.Exists(filePath.Replace("/build/Android/Android.apk", "/build")));
                }

                Debug.Log("From: " + tempDirectoryPath + " || to: " + filePath.Replace("/build/Android/Android.apk", "/build"));
                Directory.Move(tempDirectoryPath, filePath.Replace("/build/Android/Android.apk", "/build"));
            }

            #if !UNITY_ANDROID
            if (buildTarget == BuildTarget.iOS)
            {
                string projPath = filePath + "/Unity-iPhone.xcodeproj/project.pbxproj";
                PBXProject proj = new PBXProject();
                string source = File.ReadAllText(projPath);
                proj.ReadFromString(source);
                string targetGuid = proj.GetUnityMainTargetGuid();
                proj.AddBuildProperty(targetGuid, "ENABLE_BITCODE", "false");
                proj.AddBuildProperty(targetGuid, "OTHER_LDFLAGS", "-Wl,-U,_FlutterUnityPluginOnMessage");
                Debug.Log("UnityFramework: " + proj.GetUnityFrameworkTargetGuid());
                Debug.Log("Data: " + proj.FindFileGuidByProjectPath("/Data"));
                //proj.file
                Debug.Log(source.Substring(source.IndexOf("/* Data in Resources */") - 25, 24));
                //proj.AddFileToBuild();
                
                var unityFrameworkTargetGuid = proj.GetUnityFrameworkTargetGuid();
                var stringToFind = "/* Data in Resources */ = {isa = PBXBuildFile; fileRef = ";
                var dataFolderGuid = source.Substring(source.IndexOf(stringToFind) + stringToFind.Length, 24);
                proj.AddFileToBuildSection(unityFrameworkTargetGuid, proj.GetResourcesBuildPhaseByTarget(unityFrameworkTargetGuid), dataFolderGuid);

                //proj.AddFileToBuild(proj.GetUnityFrameworkTargetGuid(), source.Substring(source.IndexOf("/* Data in Resources */") - 25, 24));
                File.WriteAllText(projPath, proj.WriteToString());
                
                
                
                
                var tempDirectoryPath = filePath.Replace("/build/iOS/iOS", "/temp");
                Debug.Log("Orig Path: " + filePath + " || temp Path: " + tempDirectoryPath);
                
                
                Directory.Move(filePath, tempDirectoryPath);

                if (Directory.Exists(filePath.Replace("/build/iOS/iOS", "/build")))
                {
                    Debug.Log("Deleting build folder: " + filePath.Replace("/build/iOS/iOS", "/build"));
                    Directory.Delete(filePath.Replace("/build/iOS/iOS", "/build"), true);
                    //DeleteDirectory(filePath.Replace("/build/Android/Android.apk", "/build"));
                    Debug.Log("Does build folder exist? : " + Directory.Exists(filePath.Replace("/build/iOS/iOS", "/build")));
                }
                
                Debug.Log("From: " + tempDirectoryPath + " || to: " + filePath.Replace("/build/iOS/iOS", "/build"));
                Directory.Move(tempDirectoryPath, filePath.Replace("/build/iOS/iOS", "/build"));
            }
            #endif

            ReportSummary(buildSummary);
            ExitWithResult(buildSummary.result);
        }
        
        private static void DeleteDirectory(string target_dir)
        {
            string[] files = Directory.GetFiles(target_dir);
            string[] dirs = Directory.GetDirectories(target_dir);

            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in dirs)
            {
                DeleteDirectory(dir);
            }

            Directory.Delete(target_dir, false);
        }

        private static void ReportSummary(BuildSummary summary)
        {
            Console.WriteLine(
                $"{Eol}" +
                $"###########################{Eol}" +
                $"#      Build results      #{Eol}" +
                $"###########################{Eol}" +
                $"{Eol}" +
                $"Duration: {summary.totalTime.ToString()}{Eol}" +
                $"Warnings: {summary.totalWarnings.ToString()}{Eol}" +
                $"Errors: {summary.totalErrors.ToString()}{Eol}" +
                $"Size: {summary.totalSize.ToString()} bytes{Eol}" +
                $"{Eol}"
            );
        }

        private static void ExitWithResult(BuildResult result)
        {
            switch (result)
            {
                case BuildResult.Succeeded:
                    Console.WriteLine("Build succeeded!");
                    EditorApplication.Exit(0);
                    break;
                case BuildResult.Failed:
                    Console.WriteLine("Build failed!");
                    EditorApplication.Exit(101);
                    break;
                case BuildResult.Cancelled:
                    Console.WriteLine("Build cancelled!");
                    EditorApplication.Exit(102);
                    break;
                case BuildResult.Unknown:
                default:
                    Console.WriteLine("Build result is unknown!");
                    EditorApplication.Exit(103);
                    break;
            }
        }
    }
}