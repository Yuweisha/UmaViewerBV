using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Names, casing and defaults follow the installed native key declarations/ctors.
    [Serializable] public class LiveTimelineKeyVortexData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.Vortex;
        public bool IsEnable;
        public Vector4 Area=new Vector4(.5f,.5f,.5f,.5f);
        public float RotVolume;
        public float DepthClip=1;
    }
    [Serializable] public class LiveTimelineKeyVortexDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyVortexData>{}
    [Serializable] public class LiveTimelineKeyFluctuationData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.Fluctuation;
        public const int ATTR_DISABLE_GRAPHICSETTINGS=65536;
        public bool IsEnable;
        public Vector2 MoveDirection=Vector2.up;
        public float MovePower=.05f,Power=.01f,DepthClip=.3f;
        public bool DisableGraphicSettings=>((int)attribute&ATTR_DISABLE_GRAPHICSETTINGS)!=0;
    }
    [Serializable] public class LiveTimelineKeyFluctuationDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyFluctuationData>{}
    [Serializable] public class LiveTimelineKeyHatchingData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.Hatching;
        public int BlendMode;
        public float BlendAlpha=.5f;
        public Texture2D NoiseTexture;
        public float StrokeTiling=4,StrokeHeight=.1f,StrokeAngle=60,StrokeLength=.1f;
        public Vector2 CircleMaskOffset;
        public float CircleMaskSize;
        public Vector2 CircleMaskStretchRatio=Vector2.one;
        public float CircleMaskAngle,CircleMaskInnerRatio=.5f,CircleMaskInnerEffect;
    }
    [Serializable] public class LiveTimelineKeyHatchingDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyHatchingData>{}
    [Serializable] public class LiveTimelineKeyLensDistortionData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.LensDistortion;
        public float Intensity,IntensityX=1,IntensityY=1,CenterX,CenterY,Scale=1;
    }
    [Serializable] public class LiveTimelineKeyLensDistortionDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyLensDistortionData>{}
}
