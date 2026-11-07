using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class Director
    {
        readonly Dictionary<UmaContainerCharacter, FakeLightReceiver> _fakeLightReceivers =
            new Dictionary<UmaContainerCharacter, FakeLightReceiver>();
        readonly List<FakeLightSource> _fakeLightSources = new List<FakeLightSource>();
        MaterialPropertyBlock _fakeLightMaterialBlock;

        void SetFakeLightReceiveSetting(UmaContainerCharacter character, in BgColor1UpdateInfo info)
        {
            if (!_fakeLightReceivers.TryGetValue(character, out var receiver))
                _fakeLightReceivers.Add(character, receiver = new FakeLightReceiver());
            receiver.SetReceiveSetting(info.IsEnabledFakeLight, info.IsFakeLightOutline,
                info.FakeLightRate, info.FakeLightReceiveColor, info.FakeLightBlendMode);
        }

        void AlterLateUpdateFakeLight()
        {
            if (_stageController == null) return;
            _stageController.GetComponentsInChildren(true, _fakeLightSources);
            foreach (var source in _fakeLightSources)
                if (source != null) source.UpdateRawFakeLightParam();
            var block = _fakeLightMaterialBlock ?? (_fakeLightMaterialBlock = new MaterialPropertyBlock());
            foreach (var character in CharaContainerScript)
            {
                if (character == null || !_fakeLightReceivers.TryGetValue(character, out var receiver)) continue;
                receiver.ClearValidLights();
                if (receiver.IsEnabled)
                    foreach (var source in _fakeLightSources)
                    {
                        if (source == null) continue;
                        var value = source.GetRawFakeLightParam();
                        if (!value.IsActive) continue;
                        bool intersects = false;
                        foreach (Renderer renderer in character.Renderers)
                            if (renderer != null && FakeLightReceiver.IntersectsCapsuleAABB(value.StartPosition,
                                value.EndPosition, value.StartRadius, value.EndRadius, renderer.bounds))
                            { intersects = true; break; }
                        if (intersects) receiver.UpdateFakeLight(source, value.StartPosition, value.EndPosition,
                            value.StartIntensity, character.transform.position);
                    }
                receiver.Apply(character.Renderers, block);
            }
        }
    }
}
