using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using Gallop.RenderPipeline;
using UnityEngine;

namespace Gallop.Live
{
    // Original ModelController 52ac8a0/52ad340/52af3d0. This owns cached
    // face/global state; the timeline events do not overwrite each other.
    public sealed partial class LiveCharacterLightingRuntime
    {
        public enum Destination { None, FaceSkin, Hair, Mayu, Eye, HairBillboard, Body }
        public sealed class Binding
        {
            public Renderer renderer;
            public Material material;
            public Destination destination;
        }
        public readonly List<Binding> Bindings=new List<Binding>();
        public FacialToonUpdateInfo Facial=new FacialToonUpdateInfo {
            CheekPretenseThreshold=.775f,NosePretenseThreshold=.775f,HairNormalBlend=1,
            EyeToonStep=.4f,EyeToonFeather=.01f,EyeSaturation=1 };
        public bool UseGlobalDirection;
        public Quaternion GlobalRotation=Quaternion.identity;
        public bool UseGlobalOffset;
        public Quaternion GlobalOffset=Quaternion.identity;
        public Vector3 CachedFaceDirection=Vector3.forward;
        public void SetFacial(ref FacialToonUpdateInfo info)
        {
            Facial=info;
            // Original SetFaceDirectionalLightAngle 52a6650 uses forward.
            CachedFaceDirection=Quaternion.Euler(info.OriginalDirectionalLightDir)*Vector3.forward;
        }
        public void SetGlobal(Quaternion rotation) { UseGlobalDirection=true;GlobalRotation=rotation; }
        public void Bind(IList<Renderer> renderers)
        {
            Bindings.Clear();if(renderers==null)return;
            bool hasHair=false;
            foreach(var renderer in renderers)if(renderer!=null&&renderer.name=="M_Hair")hasHair=true;
            foreach(var renderer in renderers)
            {
                if(renderer==null)continue;
                foreach(var material in renderer.materials)
                {
                    if(material==null)continue;
                    material.name=material.name.Replace(" (Instance)","");
                    var destination=Classify(renderer.name,material,hasHair);
                    if(destination!=Destination.None)Bindings.Add(new Binding{renderer=renderer,material=material,destination=destination});
                }
            }
        }
        public static Destination Classify(string name,Material material,bool hasHair)
        {
            if(LiveModelRenderQueue.TryAlpha(name,out int offset,out var second))
            {
                if(second==LiveModelRenderQueue.Part.Hair)return Destination.Hair;
                if(second==LiveModelRenderQueue.Part.Body)return Destination.Body;
                return material.name.EndsWith("_Alpha"+offset,StringComparison.Ordinal)?Destination.Body:Destination.None;
            }
            // Original PartsHolder gets the eye material separately from the
            // M_Face material array. Actual source receipts include both slots.
            if(name=="M_Eye"||(name=="M_Face"&&material.name.EndsWith("_eye",StringComparison.Ordinal)))return Destination.Eye;
            if(!LiveModelRenderQueue.TryPart(name,material,hasHair,out var part))return Destination.None;
            switch(part)
            {
                case LiveModelRenderQueue.Part.FaceSkin:case LiveModelRenderQueue.Part.FaceProp:return Destination.FaceSkin;
                case LiveModelRenderQueue.Part.Hair:case LiveModelRenderQueue.Part.HairProp:return Destination.Hair;
                case LiveModelRenderQueue.Part.Mayu:return name=="M_Mayu"?Destination.Mayu:Destination.None;
                case LiveModelRenderQueue.Part.HairBillboard:return Destination.HairBillboard;
                case LiveModelRenderQueue.Part.Body:case LiveModelRenderQueue.Part.Tail:
                case LiveModelRenderQueue.Part.Attachment:case LiveModelRenderQueue.Part.BodyBillboard:return Destination.Body;
                default:return Destination.None;
            }
        }
        static int Id(int native)=>ShaderManager.GetPropertyId((ShaderManager.PropertyId)native);
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
        static Vector3 Center(Vector3 position,Vector3 up,Vector3 forward,float y,float z)=>new Vector3(
            F(F(F(up.x*y)+position.x)+F(forward.x*z)),F(F(F(up.y*y)+position.y)+F(forward.y*z)),F(F(F(up.z*y)+position.z)+F(forward.z*z)));
        static Vector3 TubeCenter(Vector3 position,Vector3 forward,float z)=>new Vector3(
            F(position.x+F(forward.x*z)),F(position.y+F(forward.y*z)),F(position.z+F(forward.z*z)));
        public static void WriteFaceScalars(Material material,FacialToonUpdateInfo state,int useDirection,bool faceSkin)
        {
            material.SetFloat(Id(143),state.CheekPretenseThreshold);material.SetFloat(Id(144),state.NosePretenseThreshold);
            if(faceSkin)material.SetFloat(Id(145),state.NosePretenseThreshold>0?1:0);
            material.SetFloat(Id(146),state.CylinderBlend);material.SetFloat(Id(147),state.HairNormalBlend);material.SetInt(Id(138),useDirection);
        }
        public static void WriteEye(Material material,float step,float feather,float saturation,bool enabled=true)
        {
            if(!enabled)return;
            material.SetFloat(Id(51),Mathf.Clamp01(step));material.SetFloat(Id(52),Mathf.Clamp(feather,.0001f,1));material.SetFloat(Id(175),Mathf.Clamp01(saturation));
        }
        public void Apply(Vector3 position,Vector3 up,Vector3 forward,Quaternion headRotation,Vector3 finalOffsets)
        {
            ApplyCore(position,up,forward,headRotation,finalOffsets);
        }
        void ApplyCore(Vector3 position,Vector3 up,Vector3 forward,Quaternion headRotation,Vector3 finalOffsets)
        {
            int faceMode=(int)Facial.UseOriginalDirectionalLight;
            int useFace=(faceMode>0||UseGlobalDirection)?1:0,useHair=UseGlobalDirection?1:0;
            Vector3 globalDirection=GlobalRotation*Vector3.back;
            if(UseGlobalOffset)globalDirection=GlobalOffset*globalDirection;
            Vector3 faceDirection=faceMode==2?headRotation*CachedFaceDirection:CachedFaceDirection;
            if(faceMode<1&&UseGlobalDirection)faceDirection=globalDirection;
            bool writeFaceDirection=faceMode==1||faceMode==2||(faceMode<1&&UseGlobalDirection);
            Vector3 hairCenter=Center(position,up,forward,finalOffsets.x,finalOffsets.y);
            Vector3 faceCenter=TubeCenter(position,forward,finalOffsets.z);
            foreach(var binding in Bindings)
            {
                var material=binding.material;if(material==null)continue;
                switch(binding.destination)
                {
                    case Destination.Body:
                        material.SetInt(Id(138),useHair);if(UseGlobalDirection)material.SetVector(Id(139),globalDirection);continue;
                    case Destination.Eye:
                        WriteEye(material,Facial.EyeToonStep,Facial.EyeToonFeather,Facial.EyeSaturation);continue;
                    case Destination.FaceSkin:WriteFaceScalars(material,Facial,useFace,true);break;
                    case Destination.Hair:WriteFaceScalars(material,Facial,useHair,false);break;
                    case Destination.Mayu:material.SetFloat(Id(146),Facial.CylinderBlend);material.SetInt(Id(138),useFace);break;
                    case Destination.HairBillboard:
                        material.SetInt(Id(138),useFace);material.SetVector(Id(139),globalDirection);continue;
                    default:continue;
                }
                bool hair=binding.destination==Destination.Hair;
                // Hair writes globalDirection at the end of this same iteration.
                // Keep both publication phases; only omit this overwritten value.
                if(writeFaceDirection&&(!hair||!LiveModelLateOptimization.MaterialLocal))material.SetVector(Id(139),faceDirection);
                material.SetVector(Id(140),hair?hairCenter:faceCenter);material.SetVector(Id(141),up);material.SetVector(Id(142),forward);
                // Original virtual UpdateHairLightDirection runs last.
                if(hair)material.SetVector(Id(139),globalDirection);
            }
        }
    }
}
