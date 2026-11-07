using UnityEngine;
namespace Gallop.Live.Cutt
{
    // Original LiveTimelineUtil.GetAnimationTime 0x4f32980. Keep frame-time
    // accumulation and float32 operation order, including the same-frame branch.
    public static class NativeLiveMotionTime
    {
        static float F(float value) => LiveTimelineControl.MonitorFloat32(value);
        static float Scale(LiveTimelineKeyTimescaleDataList keys, int frame)
        {
            if(keys != null) for(int i=keys.Count-1;i>=0;i--)
                if(keys.thisList[i].frame <= frame) return keys.thisList[i].Timescale;
            return 1f;
        }
        static bool DefaultScale(LiveTimelineKeyTimescaleDataList keys,int start,int end)
        {
            if(keys != null) for(int i=keys.Count-1;i>=0;i--)
            {
                var key=keys.thisList[i];if(key.frame>end)continue;
                if(key.Timescale!=1f)return false;
                if(key.frame<start)break;
            }
            return true;
        }
        public static void Calculate(out float animationTime,out float frameAnimationTime,
            int startFrame,float playSpeed,int motionHeadFrame,bool disabled,float spf,
            int currentFrame,float currentTime,LiveTimelineKeyTimescaleDataList keys,
            int previousFrame,float previousAnimationFrameTime)
        {
            float remainder=F(F(currentTime-F(currentFrame*spf))*playSpeed);
            if(!(remainder>0f))remainder=0f;
            if(DefaultScale(keys,startFrame,currentFrame))
            {
                frameAnimationTime=F(F(F(Mathf.Max(0,currentFrame-startFrame)*playSpeed)+motionHeadFrame)*spf);
                animationTime=F(frameAnimationTime+remainder);return;
            }
            float scale=1f, time=previousAnimationFrameTime;
            if(currentFrame==previousFrame) scale=disabled?1f:Scale(keys,currentFrame);
            else if(currentFrame>previousFrame && previousFrame>startFrame && previousFrame>=0)
            {
                float step=F(playSpeed*spf);
                for(int i=previousFrame;i<currentFrame;i++)
                {scale=disabled?1f:Scale(keys,i);time=F(time+F(step*scale));}
            }
            else
            {
                time=F(motionHeadFrame*spf);float step=F(playSpeed*spf);
                for(int i=startFrame;i<currentFrame;i++)
                {scale=disabled?1f:Scale(keys,i);time=F(time+F(step*scale));}
            }
            frameAnimationTime=time;animationTime=F(time+F(remainder*scale));
        }
    }
}
