using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class PreviewerCompatibilityBuild
{
    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-previewerBuildOutput");
        if (index < 0 || index + 1 >= args.Length)
            throw new ArgumentException("Missing -previewerBuildOutput");
        var group = BuildTargetGroup.Standalone;
        var previous = PlayerSettings.GetScriptingBackend(group);
        bool previousTiming = PlayerSettings.enableFrameTimingStats;
        try
        {
            PreviewerA2UShaderRetention.Ensure();
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.Mono2x);
            if (Array.IndexOf(args, "-previewerFrameTiming") >= 0) PlayerSettings.enableFrameTimingStats = true;
            string output = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (result.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Previewer build failed: " + result.summary.result);
            UnityEngine.Debug.Log("[PreviewerBuild] Succeeded: " + output);
        }
        finally { PlayerSettings.SetScriptingBackend(group, previous); PlayerSettings.enableFrameTimingStats = previousTiming; }
    }
}
