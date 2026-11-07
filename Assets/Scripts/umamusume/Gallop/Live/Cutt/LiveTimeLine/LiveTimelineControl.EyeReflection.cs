using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public partial class LiveTimelineControl
    {
        static Func<LiveTimelineKeyCameraPositionData, LiveTimelineControl, FindTimelineConfig, Vector3> EyePosition =
            (key, timeline, config) => key.GetValue(timeline);
        static Func<LiveTimelineKeyCameraLookAtData, LiveTimelineControl, Vector3, FindTimelineConfig, Vector3> EyeLookAt =
            (key, timeline, position, config) => key.GetValue(timeline, position);

        // Original 0x4ecec20 / 0x4ece870: positional interpolation uses the
        // existing camera calculators; Power/Roll/Fov use INTEGER frame ratio.
        // Enable, mask, near/far and culling are current-key discrete fields.
        public bool ApplyEyeReflection(LiveTimelineWorkSheet sheet, int groupIndex, LiveEyeReflectionDriver driver, int sizeRate)
        {
            var group = sheet.EyeCameraPosList[groupIndex];
            var keys = group?.keys;
            if (keys == null || keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)
                || !keys.EnablePlayModeTimeline(_playMode)) return false;
            FindTimelineKey(out var cur, out var next, keys, _currentFrame);
            if (!(cur is LiveTimelineKeyEyeCameraPositionData key)) return false;
            float power = key.Power, roll = key.Roll, fov = key.Fov;
            if (next is LiveTimelineKeyEyeCameraPositionData nextKey && nextKey.IsInterpolateKey())
            {
                float ratio = CalculateInterpolationValue(key, nextKey, _currentFrame);
                power = LerpWithoutClamp(power, nextKey.Power, ratio);
                roll = LerpWithoutClamp(roll, nextKey.Roll, ratio);
                fov = LerpWithoutClamp(fov, nextKey.Fov, ratio);
            }
            var context = driver.Context.Width > 0 ? driver.Context : LiveEyeReflectionContext.Default();
            context.Enabled = key.IsEnabled;
            context.ReflectionPower = power;
            context.FieldOfView = fov;
            context.NearClipPlane = key.nearClip;
            context.FarClipPlane = key.farClip;
            context.CullingMask = key.GetCullingMask();
            context.MaskTexture = key.MaskTexture;
            driver.Context = context;
            driver.Configure(key.IsEnabled, power, key.MaskTexture, sizeRate);
            var config = new FindTimelineConfig { keyType = FindTimelineConfig.KeyType.KeyDirect,
                curKey = cur, nextKey = next, posKeys = keys, extraCameraIndex = groupIndex };
            if (!CalculateCameraPos(out var position, sheet, _currentFrame, driver.CachedCamera, ref config, ref EyePosition))
                return true;
            // Original LookAt wrapper reads the owner position after the position write.
            driver.ApplyTimelinePose(position, false, Vector3.zero, roll, 1f);
            var lookKeys = sheet.EyeCameraLookAtList != null && groupIndex < sheet.EyeCameraLookAtList.Count
                ? sheet.EyeCameraLookAtList[groupIndex]?.keys : null;
            if (lookKeys == null || lookKeys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)
                || !lookKeys.EnablePlayModeTimeline(_playMode)) return true;
            FindTimelineKey(out var lookCur, out var lookNext, lookKeys, _currentFrame);
            if (!(lookCur is LiveTimelineKeyCameraLookAtData lookKey)) return true;
            config.lookAtKeys = lookKeys;
            config.curKey = lookCur;
            config.nextKey = lookNext;
            if (CalculateCameraLookAt(out var lookAt, sheet, _currentFrame, driver.CachedCamera, ref config, ref EyeLookAt, ref EyePosition))
            {
                float scale = lookKey.GetHeightRate(this);
                if (lookNext is LiveTimelineKeyCameraLookAtData lookNextKey && lookNextKey.IsInterpolateKey())
                    scale = LerpWithoutClamp(scale, lookNextKey.GetHeightRate(this),
                        CalculateInterpolationValueAtTime(lookKey, lookNextKey, _currentLiveTime));
                driver.ApplyTimelinePose(position, true, lookAt, roll, scale);
            }
            return true;
        }
    }
}
