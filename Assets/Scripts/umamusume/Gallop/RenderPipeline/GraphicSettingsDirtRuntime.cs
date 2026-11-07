using UnityEngine;

namespace Gallop
{
    // Dirt subset of the original persistent GraphicSettings instance. Model
    // setters only buffer colors; its independent Update uploads the globals.
    public sealed class LiveDirtGlobalSettings
    {
        public Color DirtColor { get; private set; }
        public Color RimSpecularColor { get; private set; }
        public Color ToonColor { get; private set; }

        public void ResetDirtParameters()
        {
            // Original cctor defaults, then DefaultDirt0x4af37b0 uploads them.
            DirtColor = new Color(.35f, .254f, .215f, 1f);
            RimSpecularColor = new Color(.6f, .451f, .384f, 1f);
            ToonColor = new Color(.28f, .19f, .14f, 1f);
            UpdateDirtParameters();
        }

        public void SetColors(Color dirt, Color rimSpecular, Color toon)
        {
            DirtColor = dirt;
            RimSpecularColor = rimSpecular;
            ToonColor = toon;
        }

        public void UpdateDirtParameters()
        {
            Upload(ShaderManager.PropertyId._GlobalDirtColor, DirtColor);
            Upload(ShaderManager.PropertyId._GlobalDirtRimSpecularColor, RimSpecularColor);
            Upload(ShaderManager.PropertyId._GlobalDirtToonColor, ToonColor);
        }

        static void Upload(ShaderManager.PropertyId property, Color color)
        {
            Shader.SetGlobalColor(ShaderManager.GetPropertyId(property), color);

        }
    }

    public sealed class GraphicSettingsDirtRuntime : MonoBehaviour
    {
        public LiveDirtGlobalSettings State { get; } = new LiveDirtGlobalSettings();

        bool initialized;
        public static void EnsureExists()
        {
            if (GraphicSettings.DirtSettings != null)
                return;
            var owner = new GameObject("GraphicSettings Dirt Runtime");
            owner.AddComponent<GraphicSettingsDirtRuntime>().Initialize();
            if (Application.isPlaying)
                DontDestroyOnLoad(owner);
        }

        public void Initialize()
        {
            GraphicSettings.DirtSettings = State;
            if (initialized)
                return;
            initialized = true;
            State.ResetDirtParameters();
        }

        void Awake() => Initialize();
        public void Tick() => State.UpdateDirtParameters();
        void Update() => Tick();
        public void Release()
        {
            if (ReferenceEquals(GraphicSettings.DirtSettings, State))
                GraphicSettings.DirtSettings = null;
        }

        void OnDestroy() => Release();
    }
}
