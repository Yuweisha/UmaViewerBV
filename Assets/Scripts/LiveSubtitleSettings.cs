using System;

public static class LiveSubtitleSettings
{
    public static bool Enabled => Config.Instance == null || Config.Instance.LiveSubtitles;
    public static event Action<bool> Changed;
    public static void SetEnabled(bool enabled,bool save=true)
    {
        if(Config.Instance==null)new Config();
        Config.Instance.LiveSubtitles=enabled;
        Changed?.Invoke(enabled);
        if(save)Config.Instance.UpdateConfig(false);
    }
}

