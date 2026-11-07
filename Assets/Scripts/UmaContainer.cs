using Gallop;
using Gallop.Live.Cutt;
using RootMotion.Dynamics;
using RootMotion.FinalIK;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class UmaContainer : MonoBehaviour
{
    public Transform Position;

    [Header("Live")]
    public bool IsLive = false;
    public bool LiveVisible = true;
    public bool IsBackdancer;
    public LiveTimelineCharaLocator LiveLocator;

    private readonly Dictionary<Renderer, bool> _liveVisibilityEnabled =
        new Dictionary<Renderer, bool>();
    private readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> _liveShadowModes =
        new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();

    // Formation visibility switches the complete model, matching the native
    // CharacterObject.ApplyVisible path. Renderers may be created/replaced by
    // the model merge after the first hide, so every call re-scans the root.
    public void SetLiveVisible(bool visible)
    {
        if (!visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!_liveVisibilityEnabled.ContainsKey(r))
                {
                    _liveVisibilityEnabled[r] = r.enabled;
                    _liveShadowModes[r] = r.shadowCastingMode;
                }
                if (r.enabled) r.enabled = false;
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            LiveVisible = false;
            Gallop.Live.Director.instance?.NotifyLiveCharacterVisible(this, false);
            return;
        }
        foreach (var pair in _liveVisibilityEnabled)
        {
            var r = pair.Key;
            if (r == null) continue;
            r.enabled = pair.Value;
            r.shadowCastingMode = _liveShadowModes[r];
        }
        _liveVisibilityEnabled.Clear();
        _liveShadowModes.Clear();
        LiveVisible = true;
        Gallop.Live.Director.instance?.NotifyLiveCharacterVisible(this, true);
    }

    [Header("Other")]
    public CharaShaderEffectData HeadShaderEffectData;
    public CharaShaderEffectData BodyShaderEffectData;
    public List<MaterialHelper> Materials = new List<MaterialHelper>();
    public List<Renderer> Renderers = new List<Renderer>();

    protected UmaViewerBuilder Builder => UmaViewerBuilder.Instance;
    protected UmaViewerMain Main => UmaViewerMain.Instance;
    protected UmaViewerUI UI => UmaViewerUI.Instance;

    public class MaterialHelper
    {
        public Material Mat;
        public UmaUIContainer Toggle;
        public Dictionary<Renderer, List<int>> Renderers;
    
        public void ToggleMaterials(bool value)
        {
            foreach(var rend in Renderers)
            {
                Debug.Log(rend.Key.name);
                Material[] mat = new Material[rend.Key.sharedMaterials.Length];
                for(int i = 0; i < mat.Length; i++)
                {
                    //if material slot is not in list, keep current material
                    //if material slot in list and toggle is on - assign original
                    //if material slot in list and toggle is off - assign invisible
                    mat[i] = rend.Value.Contains(i) ? (value? Mat : UmaViewerBuilder.Instance.TransMaterialCharas ): rend.Key.sharedMaterials[i];
                }

                rend.Key.sharedMaterials = mat;
            }
        }
    }
}
