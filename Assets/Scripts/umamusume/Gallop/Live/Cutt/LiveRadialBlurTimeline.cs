using UnityEngine;
using Gallop.RenderPipeline;
namespace Gallop.Live.Cutt
{
    public static class LiveRadialBlurTimeline
    {
        public static RadialBlurPass.Parameter Evaluate(LiveTimelineKeyRadialBlurDataList keys,
            float frame, TimelinePlayerMode playMode)
        {
            return TryEvaluate(keys,(int)frame,playMode,out var p)?p:RadialBlurPass.Parameter.Default();
        }
        public static bool TryEvaluate<TK>(LiveTimelineKeyDataListTemplate<TK> keys,int frame,TimelinePlayerMode mode,out RadialBlurPass.Parameter p) where TK:LiveTimelineKeyRadialBlurData
        {
            p=default;
            if(!LiveNativePostTimeline.TryKeys(keys,frame,mode,out var a,out var b))return false;
            p=Sample(a,b,frame);return true;
        }
        public static RadialBlurPass.Parameter Sample(LiveTimelineKeyRadialBlurData a,LiveTimelineKeyRadialBlurData b,int frame)
        {
            var p=RadialBlurPass.Parameter.Default();
            float t = b != null && b.IsInterpolateKey() ? LiveTimelineControl.CalculateInterpolationValue(a, b, frame) : 0f;
            b = b ?? a;
            p.Type = a.moveBlurType;
            p.Downsample = a.radialBlurDownsample;
            p.Iteration = a.radialBlurIteration;
            p.Offset = Vector2.Lerp(a.radialBlurOffset, b.radialBlurOffset, t);
            p.EllipseDir = Vector2.Lerp(a.radialBlurEllipseDir, b.radialBlurEllipseDir, t);
            p.StartArea = Mathf.LerpUnclamped(a.radialBlurStartArea, b.radialBlurStartArea, t);
            p.EndArea = Mathf.LerpUnclamped(a.radialBlurEndArea, b.radialBlurEndArea, t);
            p.Power = Mathf.LerpUnclamped(a.radialBlurPower, b.radialBlurPower, t);
            p.RollEulerAngles = Mathf.LerpUnclamped(a.radialBlurRollEulerAngles, b.radialBlurRollEulerAngles, t);
            p.DepthPowerFront = Mathf.LerpUnclamped(a.depthPowerFront, b.depthPowerFront, t);
            p.DepthPowerBack = Mathf.LerpUnclamped(a.depthPowerBack, b.depthPowerBack, t);
            p.DepthCancelRect = Vector4.Lerp(a.depthCancelRect, b.depthCancelRect, t);
            p.DepthCancelBlendLength = Mathf.LerpUnclamped(a.depthCancelBlendLength, b.depthCancelBlendLength, t);
            // SetupRadialBlurInfo0x4ef6ec0 invokes accessors for bits16/17/18.
            p.IsEnabledDepth = ((int)a.attribute & 0x10000) != 0;
            p.IsEnabledDepthCancelRect = ((int)a.attribute & 0x20000) != 0;
            p.IsExpandDepthCancelRect = ((int)a.attribute & 0x40000) != 0;
            return p;
        }
        public static void Apply(ref RadialBlurPass.Parameter p,RadialBlurPass.Parameter info)
        {
            // This Viewer block contains only timeline-owned fields.
            // The render event is owned by the RadialBlurPass instance.
            p=info;
        }
    }
}
