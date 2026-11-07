using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gallop.Live;
using UnityEngine;
#if UNITY_STANDALONE || UNITY_EDITOR
using SFB;
#endif

/// <summary>Shared character export entry for normal viewing and Live.</summary>
public sealed class UnifiedExportPanel : MonoBehaviour
{
    // Temporarily disabled for the Live playback release.
    public static readonly bool FeatureEnabled = false;
    private bool _open;
    private int _characterIndex;
    private string _lastPmx;
    private string _status = "";
    private GlbAnimationRecorder _recorder;
    private Rect _window = new Rect(20, 70, 340, 330);

    private UmaContainerCharacter[] Characters()
    {
        if (Director.instance != null && Director.instance.CharaContainerScript != null)
            return Director.instance.CharaContainerScript.Where(c => c != null).ToArray();
        var character = UmaViewerBuilder.Instance?.CurrentUMAContainer;
        return character == null ? new UmaContainerCharacter[0] : new[] { character };
    }

    private void OnGUI()
    {
        if (!FeatureEnabled) return;
        if (GUI.Button(new Rect(20, 25, 160, 32), "Export / Physics")) _open = !_open;
        if (_open) _window = GUI.Window(GetInstanceID(), _window, Draw, "Unified character tools");
    }

    private void Draw(int id)
    {
        var characters = Characters();
        if (characters.Length > 0)
        {
            _characterIndex = Mathf.Clamp(_characterIndex, 0, characters.Length-1);
            var c = characters[_characterIndex];
            GUILayout.Label($"{_characterIndex+1}/{characters.Length}: {c.name}");
            if (characters.Length > 1 && GUILayout.Button("Next character")) _characterIndex = (_characterIndex+1)%characters.Length;
            GUILayout.Label($"Active physics: {c.ActivePhysicsBackend}");
            if (GUILayout.Button("Export Blender model (.glb)")) Run(() => { string p=Choose(c,"glb"); GlbCharacterExporter.Export(c,p); _status="Saved: "+p; });
            if (GUILayout.Button("Export native face package (.json + .glb)")) Run(() => { string p=Choose(c,"json"); NativeFaceExporter.Export(c,p); _status="Saved: "+p; });
            if (GUILayout.Button("Export native Live mouth timeline (.json)")) Run(() => { string p=Choose(c,"json"); NativeLipExporter.Export(c,_characterIndex,p); _status="Saved: "+p; });
            if (GUILayout.Button("Export MMD model (.pmx)")) Run(() => { string p = Choose(c,"pmx"); if (p != null) { ModelExporter.ExportModel(c,p); _lastPmx=p; _status="Saved: "+p; } });
            if (_recorder != null && _recorder.IsRecording)
            {
                if (GUILayout.Button("Stop and save GLB animation")) Run(() => _recorder.Finish());
                if (GUILayout.Button("Cancel GLB recording")) _recorder.Cancel();
            }
            else if (GUILayout.Button("Record animation + physics (.glb)")) Run(() => {
                string path = Choose(c,"glb"); if (path == null) return;
                _recorder = c.GetComponent<GlbAnimationRecorder>() ?? c.gameObject.AddComponent<GlbAnimationRecorder>();
                _recorder.Begin(c,path);
            });
        }
        else GUILayout.Label("Load a character or Live first.");
        if (Config.Instance != null)
        {
            GUILayout.Label("Physics for next character / Live load:");
            int backend = GUILayout.Toolbar((int)Config.Instance.PhysicsBackend,new[] {"CySpring native","DynamicBone"});
            if (backend != (int)Config.Instance.PhysicsBackend)
            {
                Config.Instance.PhysicsBackend = (CharacterPhysicsBackend)backend;
                Config.Instance.UpdateConfig(false); _status = "Reload the character / Live to apply.";
            }
        }
        if (GUILayout.Button("Open PMX workbench")) Run(OpenWorkbench);
        GUILayout.Label(_status);
        GUI.DragWindow();
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (OperationCanceledException) { }
        catch (Exception e) { _status=e.Message; UnityEngine.Debug.LogException(e); }
    }

    private string Choose(UmaContainerCharacter c, string extension)
    {
#if UNITY_STANDALONE || UNITY_EDITOR
        string path = StandaloneFileBrowser.SaveFilePanel("Save "+extension, "", c.name, extension);
        if (string.IsNullOrEmpty(path)) throw new OperationCanceledException();
        return path;
#else
        throw new NotSupportedException("Desktop export only");
#endif
    }

    private void OpenWorkbench()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath,"..","ExternalTools","pmx"));
        string entry = Path.Combine(root,"workflow_gui.py");
        if (!File.Exists(entry)) throw new FileNotFoundException("Place ExternalTools/pmx in the Unity project or beside the built executable.", entry);
        var start = new ProcessStartInfo { FileName="python", Arguments="\""+entry+"\"", WorkingDirectory=root, UseShellExecute=false };
        // A dedicated process has its own selected model; no pipeline rewrites an export automatically.
        if (!string.IsNullOrEmpty(_lastPmx)) start.EnvironmentVariables["PMX_INITIAL_MODEL"] = _lastPmx;
        Process.Start(start); _status="PMX workbench opened. Choose only the processing steps you need.";
    }
}
