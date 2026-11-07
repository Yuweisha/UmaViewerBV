using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public static class UmaStandalone
{
    public static bool IsSupportedPlatform => UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsPlayer
        || UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor;

    public static IEnumerator Initialize(Action<string, UIMessageType> progress, Action<bool> completed)
        => Run(InitializeCore(progress, completed), progress, completed);

    public static IEnumerator Rebuild(Action<string, UIMessageType> progress, Action<bool> completed)
        => Run(RebuildCore(progress, completed), progress, completed);

    static IEnumerator Run(IEnumerator operation, Action<string, UIMessageType> progress, Action<bool> completed)
    {
        // Unity stops a coroutine on an uncaught exception. Report IO/SQLite failures
        // through the same startup result and dispose any active rebuild instead.
        var pending = new Stack<IEnumerator>();
        pending.Push(operation);
        try
        {
            while (pending.Count > 0)
            {
                var routine = pending.Peek();
                bool moved = false;
                Exception failure = null;
                try { moved = routine.MoveNext(); }
                catch (Exception ex) { failure = ex; }
                if (failure != null)
                {
                    progress?.Invoke("Standalone initialization failed: " + failure.Message, UIMessageType.Error);
                    completed(false);
                    yield break;
                }
                if (!moved) { (pending.Pop() as IDisposable)?.Dispose(); continue; }
                if (routine.Current is IEnumerator nested && !(nested is UnityEngine.CustomYieldInstruction)) pending.Push(nested);
                else yield return routine.Current;
            }
        }
        finally
        {
            while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
        }
    }

    static IEnumerator RebuildCore(Action<string, UIMessageType> progress, Action<bool> completed)
    {
        if (!IsSupportedPlatform || Config.Instance.Region != Region.Jp)
        {
            progress?.Invoke("Standalone database rebuilding currently supports the Japan Windows CDN only.", UIMessageType.Error);
            completed(false);
            yield break;
        }
        UmaDatabaseController.ResetInstance();
        using var manifest = new ManifestDB();
        yield return manifest.UpdateResourceVersion(progress);
        completed(manifest.Succeeded);
    }

    static IEnumerator InitializeCore(Action<string, UIMessageType> progress, Action<bool> completed)
    {
        var config = Config.Instance;
        if (!UmaDatabaseController.TrySelectDatabasePaths(config.MainPath, WorkMode.Standalone, out var meta, out var master))
        {
            bool built = false;
            yield return RebuildCore(progress, result => built = result);
            if (!built) { completed(false); yield break; }
            UmaDatabaseController.TrySelectDatabasePaths(config.MainPath, WorkMode.Standalone, out meta, out master);
        }

        // Native encrypted meta already carries its own keys. Only repair the rebuilt file.
        if (Path.GetFileName(meta) == "meta_umaviewer" && UmaKeySync.NeedsSync(meta))
        {
            UmaDatabaseController.ResetInstance();
            bool synced = false;
            yield return UmaKeySync.Ensure(meta, result => synced = result, progress);
            if (!synced) { completed(false); yield break; }
        }
        UmaDatabaseController.ResetInstance();
        var database = UmaDatabaseController.Instance;
        bool ready = database.MetaEntries != null && database.CharaData != null;
        progress?.Invoke(ready ? "Standalone databases ready" : "Standalone database loading failed", ready ? UIMessageType.Default : UIMessageType.Error);
        completed(ready);
    }
}
