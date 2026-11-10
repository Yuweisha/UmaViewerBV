#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

/// <summary>
/// 本地 Android APK 构建入口（CI 的 license 激活失败时用这个出包）。
/// 用法：
///   Unity.exe -batchmode -nographics -quit -projectPath . \
///     -executeMethod LocalAndroidBuild.Run -apkOutput C:/path/UmaViewer.apk -logFile build.log
/// </summary>
public static class LocalAndroidBuild
{
    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-apkOutput");

        if (index < 0 || index + 1 >= args.Length)
            throw new ArgumentException("Missing -apkOutput");

        string output = Path.GetFullPath(args[index + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");

        BuildTargetGroup group = BuildTargetGroup.Android;
        ScriptingImplementation previous = PlayerSettings.GetScriptingBackend(group);
        bool previousBundle = EditorUserBuildSettings.buildAppBundle;

        try
        {
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
            EditorUserBuildSettings.buildAppBundle = false;   // 要 APK，不要 AAB

            BuildPlayerOptions options = new()
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Android build failed: " + report.summary.result);

            UnityEngine.Debug.Log($"[LocalAndroid] Succeeded: {output} "
                + $"({report.summary.totalSize / 1048576.0:F1} MB, {report.summary.totalTime.TotalMinutes:F1} min)");
        }
        finally
        {
            PlayerSettings.SetScriptingBackend(group, previous);
            EditorUserBuildSettings.buildAppBundle = previousBundle;
        }
    }
}
#endif
