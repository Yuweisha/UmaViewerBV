using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public sealed class LiveTimelineKeyTransparentCameraData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.TransparentCamera;
        public bool Enable, IsOrthographic, IsDefaultTransform;
        public float OrthographicSize = 5f;
    }
    [Serializable]
    public sealed class LiveTimelineKeyTransparentCameraDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyTransparentCameraData> { }

    public partial class LiveTimelineControl
    {
        public bool TransparentCameraEnabled { get; private set; }
        public bool TransparentCameraOrthographic { get; private set; }
        public bool TransparentCameraDefaultTransform { get; private set; }
        public float TransparentCameraOrthographicSize { get; private set; } = 5f;

        // Native AlterUpdate 0x4ee8610 resets these per frame. Explicit keys
        // OR-enable the camera; late Effect keys may independently OR-enable it.
        void AlterUpdate_TransparentCamera()
        {
            TransparentCameraEnabled = false;
            TransparentCameraOrthographic = false;
            TransparentCameraDefaultTransform = false;
            TransparentCameraOrthographicSize = 5f;
            var keys = CurrentWorkSheet?.TransparentCameraKeys;
            if (keys == null || keys.Count == 0 ||
                keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode)) return;
            FindTimelineKey(out var current, out var next, keys, _currentFrame);
            var a = current as LiveTimelineKeyTransparentCameraData;
            if (a == null) return;
            var b = next as LiveTimelineKeyTransparentCameraData;
            TransparentCameraEnabled |= a.Enable;
            TransparentCameraOrthographic = a.IsOrthographic;
            TransparentCameraDefaultTransform = a.IsDefaultTransform;
            float t = b != null && b.IsInterpolateKey() ? CalculateInterpolationValue(a, b, _currentFrame) : 0f;
            TransparentCameraOrthographicSize = b != null
                ? Mathf.LerpUnclamped(a.OrthographicSize, b.OrthographicSize, t) : a.OrthographicSize;
        }
    }
}
