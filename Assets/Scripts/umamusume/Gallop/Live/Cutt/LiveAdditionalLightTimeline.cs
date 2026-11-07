using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // GameAssembly 0x4f0a670 / original worksheet TypeTree. Keep serialized names.
    [Serializable]
    public sealed class LiveTimelineKeyData_AdditionalLight : LiveTimelineKeyWithInterpolate
    {
        public enum MyLightType { Directional, Spot, Point }
        public Vector3 Position;
        public Vector3 Rotate;
        public bool IsEnable;
        public MyLightType Type;
        public float Range = 10f;
        public int SpotAngle = 30;
        public int IndirectMultiplier = 1;
        public LightShadows ShadowType = LightShadows.Hard;
        public float Strength = 1f;
        public float Bias = .05f;
        public float NormalBias = .4f;
        public float NearPlane = .2f;
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.AdditionalLight;
    }

    [Serializable]
    public sealed class LiveTimelineKeyAdditionalLightList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyData_AdditionalLight> { }

    [Serializable]
    public sealed class LiveTimelineAdditionalLight : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyAdditionalLightList keys = new LiveTimelineKeyAdditionalLightList();
        public LiveTimelineAdditionalLight() : base("Light") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }

    public struct AdditionalLightUpdateInfo
    {
        public int Index;
        public Vector3 Position, Rotate;
        public bool IsEnable;
        public LightType Type;
        public float Range;
        public int SpotAngle, IndirectMultiplier;
        public LightShadows ShadowType;
        public float Strength, Bias, NormalBias, NearPlane;
    }

    public partial class LiveTimelineControl
    {
        public event Action<int> OnUpdateAdditionalLightCount;
        public event Action<AdditionalLightUpdateInfo> OnUpdateAdditionalLight;

        // Native 0x4ec4520: count is the number of groups, booleans/type are held,
        // integer fields truncate interpolated values. Unlike many tracks no play-mode gate.
        private void AlterUpdate_AdditionalLight(LiveTimelineWorkSheet sheet, int frame)
        {
            var groups = sheet.AdditionalLightList;
            OnUpdateAdditionalLightCount?.Invoke(groups == null ? 0 : groups.Count);
            if (groups == null || OnUpdateAdditionalLight == null) return;
            for (int index = 0; index < groups.Count; ++index)
            {
                var keys = groups[index]?.keys;
                if (keys == null || keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)) continue;
                FindTimelineKey(out var current, out var next, keys, frame);
                var a = current as LiveTimelineKeyData_AdditionalLight;
                var b = next as LiveTimelineKeyData_AdditionalLight;
                if (a == null) continue;
                float t = b != null && b.IsInterpolateKey() ? CalculateInterpolationValue(a, b, frame) : 0f;
                if (b == null) b = a;
                OnUpdateAdditionalLight(new AdditionalLightUpdateInfo {
                    Index=index, IsEnable=a.IsEnable,
                    Type=a.Type == LiveTimelineKeyData_AdditionalLight.MyLightType.Spot ? LightType.Spot :
                        a.Type == LiveTimelineKeyData_AdditionalLight.MyLightType.Point ? LightType.Point : LightType.Directional,
                    Position=Vector3.LerpUnclamped(a.Position,b.Position,t), Rotate=Vector3.LerpUnclamped(a.Rotate,b.Rotate,t),
                    Range=Mathf.LerpUnclamped(a.Range,b.Range,t), SpotAngle=(int)Mathf.LerpUnclamped(a.SpotAngle,b.SpotAngle,t),
                    IndirectMultiplier=(int)Mathf.LerpUnclamped(a.IndirectMultiplier,b.IndirectMultiplier,t), ShadowType=a.ShadowType,
                    Strength=Mathf.LerpUnclamped(a.Strength,b.Strength,t), Bias=Mathf.LerpUnclamped(a.Bias,b.Bias,t),
                    NormalBias=Mathf.LerpUnclamped(a.NormalBias,b.NormalBias,t), NearPlane=Mathf.LerpUnclamped(a.NearPlane,b.NearPlane,t)
                });
            }
        }
    }
}
