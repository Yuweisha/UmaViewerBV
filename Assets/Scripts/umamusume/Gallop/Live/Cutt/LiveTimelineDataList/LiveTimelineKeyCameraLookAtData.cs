using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyCameraLookAtData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType
        {
            get{return LiveTimelineKeyDataType.CameraLookAt;}
        }
        public LiveCameraLookAtType lookAtType;
        public Vector3 position;
        public LiveCharaPositionFlag lookAtCharaPos;
        public LiveCameraCharaParts lookAtCharaParts;
        public Vector3 charaPos;
        public Vector3[] bezierPoints;
        public float traceSpeed;
        public Vector3 CharaPositionAtStartFrame;
        public bool IsUseCharaPositionAtPrevKeyStartFrame;
        private int _charaPositionCachedFrame = -1;
        public bool IsUsePositionCache => (int)lookAtCharaParts == 20;

        public void CacheStartFramePositionIfNeed(LiveTimelineControl timeline, int cacheFrame)
        {
            if (!IsUsePositionCache || _charaPositionCachedFrame >= 0) return;
            CacheStartFramePosition(timeline, cacheFrame);
        }

        public void CacheStartFramePosition(LiveTimelineControl timeline, int cacheFrame)
        {
            CharaPositionAtStartFrame = timeline.GetPositionWithCharacters(lookAtCharaPos,
                IsUsePositionCache ? LiveCameraCharaParts.Face : LiveCameraCharaParts.Max);
            _charaPositionCachedFrame = cacheFrame;
        }

        public bool IsAttachedToProps;
        public int PropsIndex;
        public int PropsAttachNodeIndex;

        public static bool TryGetTransformAttachedToProps(bool attached, int flags, int propsIndex, int nodeIndex, out Transform target)
        {
            target = null;
            if (!attached) return false;
            var director = Gallop.Live.Director.instance;
            for (int i = 0; i < 20; i++)
                if ((flags & (1 << i)) != 0 && director.TryGetPropsCameraNode(i, propsIndex, nodeIndex, out target)) break;
            // Original outer bool means attachment mode, not successful lookup.
            return true;
        }

        public static Vector3 GetWorldPositionAttachedToProps(int flags, int propsIndex, int nodeIndex, Vector3 offset)
        {
            TryGetTransformAttachedToProps(true, flags, propsIndex, nodeIndex, out Transform target);
            return target != null ? target.position + target.rotation * offset : offset;
        }

        public static Vector3 GetCharacterWorldPosition(LiveTimelineControl timelineControl, LiveCharaPositionFlag flags,
            LiveCameraCharaParts parts, Vector3 charaPosition, Vector3 value, bool attached, int propsIndex, int nodeIndex)
        {
            if (attached) return GetWorldPositionAttachedToProps((int)flags, propsIndex, nodeIndex, value);
            switch ((int)parts)
            {
                case 11: return (timelineControl.GetPositionWithCharacters(flags, (LiveCameraCharaParts)17) + charaPosition) + value;
                case 12: return (timelineControl.GetPositionWithCharacters(flags, (LiveCameraCharaParts)18) + charaPosition) + value;
                case 13: return (timelineControl.GetPositionWithCharacters(flags, (LiveCameraCharaParts)19) + charaPosition) + value;
                case 14: return value + charaPosition;
                default: return value + timelineControl.GetPositionWithCharacters(flags, parts);
            }
        }

        private static Vector3 RoundAttachedLookAt(Vector3 value)
        {
            // Original CuttVector3_Helper.Round (0x4f154f0), 0.001 units.
            return new Vector3(Round(value.x), Round(value.y), Round(value.z));
        }

        private static float Round(float value)
        {
            float scaled = LiveTimelineControl.MonitorFloat32(value * 1000f);
            // Native nonnegative branch turns input -0 into +0; negative half
            // values that round to zero retain -0. Mathf.Round alone keeps input -0.
            if (scaled == 0f) return 0f;
            return LiveTimelineControl.MonitorFloat32(Mathf.Round(scaled) / 1000f);
        }

        public Vector3 rotation = Vector3.forward;
        public float eyeLength = 10f;
        public Vector3 offset = Vector3.zero;
        public bool newBezierCalcMethod
        {
            get => ((int)attribute & 0x10000) != 0;
            set => attribute = (LiveTimelineKeyAttribute)(value ? (int)attribute | 0x10000 : (int)attribute & ~0x10000);
        }

        public bool necessaryToUseNewBezierCalcMethod
        {
            get
            {
                if (!newBezierCalcMethod)
                {
                    return GetBezierPointCount() > 3;
                }

                return true;
            }
        }

        // Original 0x4f07380 delegates only Character keys to timeline height.
        public float GetHeightRate(LiveTimelineControl timelineControl)
        {
            return lookAtType == LiveCameraLookAtType.Character
                ? timelineControl.GetHeightRateWithCharacters(lookAtCharaPos) : 1f;
        }

        public bool HasBezier()
        {
            return bezierPoints != null && bezierPoints.Length != 0;
        }

        public int GetBezierPointCount()
        {
            if (!HasBezier())
            {
                return 0;
            }

            return bezierPoints.Length;
        }

        public Vector3 GetBezierPoint(
            int index,
            LiveTimelineControl timelineControl,
            Vector3 camPos)
        {
            if (HasBezier() && index < bezierPoints.Length)
            {
                return GetValue(timelineControl, camPos) + bezierPoints[index];
            }

            return GetValue(timelineControl, camPos);
        }

        public void GetBezierPoints(
            LiveTimelineControl timelineControl,
            Vector3 camPos,
            Vector3[] outPoints,
            int startIndex)
        {
            if (!HasBezier())
            {
                return;
            }

            // Original 0x4f06ea0 limits by total array length, then performs
            // indexed writes. Preserve sampling and partial writes before faults.
            int num = Mathf.Min(outPoints.Length, bezierPoints.Length);
            Vector3 value = GetValue(timelineControl, camPos);

            for (int i = 0; i < num; i++)
            {
                outPoints[startIndex + i] = value + bezierPoints[i];
            }
        }

        public virtual Vector3 GetValue(
            LiveTimelineControl timelineControl,
            Vector3 camPos)
        {
            return GetValue(timelineControl, lookAtType, camPos, true);
        }

        protected virtual Vector3 GetValue(
            LiveTimelineControl timelineControl,
            LiveCameraLookAtType type,
            Vector3 camPos,
            bool containOffset)
        {
            if (type == LiveCameraLookAtType.Direct) return position;
            if (type != LiveCameraLookAtType.Character) return Vector3.zero;
            if ((int)lookAtCharaParts != 20)
                return RoundAttachedLookAt(GetCharacterWorldPosition(timelineControl, lookAtCharaPos, lookAtCharaParts,
                    charaPos, position, IsAttachedToProps, PropsIndex, PropsAttachNodeIndex));
            if (!IsUseCharaPositionAtPrevKeyStartFrame && frame != timelineControl.CurrentCameraLookAtKeyFrame)
            {
                CharaPositionAtStartFrame = timelineControl.GetPositionWithCharacters(lookAtCharaPos, LiveCameraCharaParts.Face);
                _charaPositionCachedFrame = frame;
            }
            return RoundAttachedLookAt(position + CharaPositionAtStartFrame);
        }

        public Vector3 GetValue(LiveTimelineControl timelineControl)
        {
            return GetValue(timelineControl, Vector3.zero);
        }

        public Vector3 GetBezierPoint(
            int index,
            LiveTimelineControl timelineControl)
        {
            return GetBezierPoint(index, timelineControl, Vector3.zero);
        }

        public void GetBezierPoints(
            LiveTimelineControl timelineControl,
            Vector3[] outPoints,
            int startIndex)
        {
            GetBezierPoints(timelineControl, Vector3.zero, outPoints, startIndex);
        }
    }
}
