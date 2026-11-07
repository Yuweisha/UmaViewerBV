using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Numeric closure assembled by original formation parent0x4ed09e0.
    // Engine Transform, world-space and model/locator consumers are separate.
    public struct LiveFormationSample
    {
        public Color EmissiveColor;
        public float EmissiveSpeed,EmissiveEnergyScale,EmissiveIntensity,EmissiveRimPower,EmissiveRimIntensity;
        public bool IsEmissiveCenter;
        public Vector3 OffsetMax,OffsetMin,Position;
        public bool IsWorldSpace;
        public Vector3 WorldSpaceOrigin;
        public float WorldRotationY;
        public bool IsLookAtWorldOrigin;
        public Quaternion Rotation,LocalRotation;
        public float RotationY,LocalRotationY;
        public float ScaleFactor;
        public float DirtEnergyB,DirtEnergyG,DirtEnergyR;
        public Color DirtColor,DirtRimSpecularColor,DirtToonColor;

        public static LiveFormationSample Evaluate(LiveTimelineKeyFormationOffsetData current,LiveTimelineKeyFormationOffsetData next,int frame)
        {
            bool interpolate=next!=null&&next.IsInterpolateKey();
            float rate=interpolate?LiveTimelineControl.CalculateInterpolationValue(current,next,frame):0f;
            return EvaluatePrepared(current,next,interpolate,rate);
        }

        // Interpolation curves are sampled on their owning thread. The rest
        // of the formation closure only reads immutable key fields and math.
        public static LiveFormationSample EvaluatePrepared(LiveTimelineKeyFormationOffsetData current,
            LiveTimelineKeyFormationOffsetData next,bool interpolate,float rate)
        {
            var result=new LiveFormationSample
            {
                EmissiveColor=current.EmissiveColor,EmissiveSpeed=current.EmissiveScrollTimeScale,EmissiveEnergyScale=current.EmissiveScrollEnergyScale,
                EmissiveIntensity=1f,EmissiveRimPower=1f,EmissiveRimIntensity=1f,ScaleFactor=current.ScaleFactor,
                IsWorldSpace=current.IsWorldSpace,IsLookAtWorldOrigin=current.IsLookAtWorldOrigin,
                DirtEnergyB=current.DirtEnergyB,DirtEnergyG=current.DirtEnergyG,DirtEnergyR=current.DirtEnergyR,
                DirtColor=current.DirtColor,DirtRimSpecularColor=current.DirtRimSpecularColor,DirtToonColor=current.DirtToonColor
            };
            float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
            float L(float a,float b)=>F(a+F(F(b-a)*rate));
            Vector3 V(Vector3 a,Vector3 b)=>new Vector3(L(a.x,b.x),L(a.y,b.y),L(a.z,b.z));
            Color C(Color a,Color b)=>new Color(L(a.r,b.r),L(a.g,b.g),L(a.b,b.b),L(a.a,b.a));
            float rotation=current.RotationY,localRotation=current.LocalRotationY;
            result.Position=current.Position;
            if(current.isEnabledOffset){result.OffsetMax=current.offsetMaxPosition;result.OffsetMin=current.offsetMinPosition;}
            if(current.IsWorldSpace){result.WorldSpaceOrigin=current.WorldSpaceOrigin;result.WorldRotationY=current.WorldRotationY;}
            if(current.IsEmissiveColor)
            {
                result.EmissiveIntensity=current.EmissiveIntensity;result.EmissiveRimPower=current.EmissiveRimPower;result.EmissiveRimIntensity=current.EmissiveRimIntensity;result.IsEmissiveCenter=current.IsEmissiveCenter;
            }
            if(interpolate)
            {
                result.Position=V(current.Position,next.Position);rotation=L(rotation,next.RotationY);localRotation=L(localRotation,next.LocalRotationY);
                if(((int)current.attribute&0x80000)!=0&&((int)next.attribute&0x80000)!=0)result.ScaleFactor=L(current.ScaleFactor,next.ScaleFactor);
                if(current.isEnabledOffset){result.OffsetMax=V(current.offsetMaxPosition,next.offsetMaxPosition);result.OffsetMin=V(current.offsetMinPosition,next.offsetMinPosition);}
                if(current.IsWorldSpace){result.WorldSpaceOrigin=V(current.WorldSpaceOrigin,next.WorldSpaceOrigin);result.WorldRotationY=L(current.WorldRotationY,next.WorldRotationY);}
                result.EmissiveColor=C(current.EmissiveColor,next.EmissiveColor);result.EmissiveSpeed=L(current.EmissiveScrollTimeScale,next.EmissiveScrollTimeScale);result.EmissiveEnergyScale=L(current.EmissiveScrollEnergyScale,next.EmissiveScrollEnergyScale);
                if(current.IsEmissiveColor)
                {
                    result.EmissiveIntensity=L(current.EmissiveIntensity,next.EmissiveIntensity);result.EmissiveRimPower=L(current.EmissiveRimPower,next.EmissiveRimPower);result.EmissiveRimIntensity=L(current.EmissiveRimIntensity,next.EmissiveRimIntensity);
                }
                result.DirtEnergyB=L(current.DirtEnergyB,next.DirtEnergyB);result.DirtEnergyG=L(current.DirtEnergyG,next.DirtEnergyG);result.DirtEnergyR=L(current.DirtEnergyR,next.DirtEnergyR);
                result.DirtColor=C(current.DirtColor,next.DirtColor);result.DirtRimSpecularColor=C(current.DirtRimSpecularColor,next.DirtRimSpecularColor);result.DirtToonColor=C(current.DirtToonColor,next.DirtToonColor);
            }
            result.Rotation=Quaternion.AngleAxis(rotation,Vector3.up);result.LocalRotation=Quaternion.AngleAxis(localRotation,Vector3.up);
            result.RotationY=rotation;result.LocalRotationY=localRotation;
            return result;
        }
    }
}
