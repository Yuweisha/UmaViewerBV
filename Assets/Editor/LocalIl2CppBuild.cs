#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

/// <summary>
/// 本地 IL2CPP 构建入口。PreviewerCompatibilityBuild 会强制切到 Mono，
/// 那个包在 A2U 材质上渲染不对，验证 Gacha/CutIn 这类效果必须走 IL2CPP。
///
/// 用法：
///   Unity.exe -batchmode -nographics -quit -projectPath . \
///     -executeMethod LocalIl2CppBuild.Run -il2cppOutput C:/path/UmaViewer.exe -logFile build.log
/// </summary>
public static class LocalIl2CppBuild
{
    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-il2cppOutput");

        if (index < 0 || index + 1 >= args.Length)
            throw new ArgumentException("Missing -il2cppOutput");

        string output = Path.GetFullPath(args[index + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");

        BuildTargetGroup group = BuildTargetGroup.Standalone;
        ScriptingImplementation previous = PlayerSettings.GetScriptingBackend(group);

        try
        {
            PreviewerA2UShaderRetention.Ensure();
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);

            BuildPlayerOptions options = new()
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("IL2CPP build failed: " + report.summary.result);

            UnityEngine.Debug.Log($"[LocalIl2Cpp] Succeeded: {output} "
                + $"({report.summary.totalSize / 1048576.0:F1} MB, {report.summary.totalTime.TotalMinutes:F1} min)");
        }
        finally
        {
            PlayerSettings.SetScriptingBackend(group, previous);
        }
    }
}
#endif
