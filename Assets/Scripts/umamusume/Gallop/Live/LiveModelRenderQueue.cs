using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // Original Director.OnPreCullDirector 0x4e2da80 runs this per camera, including
    // mirrors/monitor/multi cameras. No LateUpdate snapshot can serve all views.
    public sealed class LiveModelRenderQueue
    {
        public enum Part { FaceSkin,Head,HeadWithoutHair,Eye,Mouth,FaceProp,Hair,HairProp,Body,Tail,Mayu,Jaw,Attachment,HeadObj,BodyBillboard,HairBillboard,Prop,Alpha }
        public struct MaterialBinding { public Material material;public Renderer renderer;public Part part;public string rendererName;public int queueOffset; }
        [Serializable] public struct Observation
        {
            public string actor,renderer,part,material;
            public int actorId,rendererId,materialId,queue,queueOffset;
        }
        public sealed class ActorBinding
        {
            public UmaContainerCharacter actor;
            public readonly List<MaterialBinding> materials=new List<MaterialBinding>();
            public readonly List<string> unverified=new List<string>();
        }
        struct DepthInfo { public int index;public float depth; }
        readonly List<ActorBinding> _actors=new List<ActorBinding>();
        struct CameraScope { public Camera camera;public int callbacks; }
        readonly List<CameraScope> _cameraScopes=new List<CameraScope>();
        DepthInfo[] _depth=Array.Empty<DepthInfo>();
        public IReadOnlyList<ActorBinding> Actors=>_actors;
        public Camera LastCamera { get; private set; }
        public int UpdateCount { get; private set; }
        public static int BaseQueue(int order)=>unchecked(76-4*order);
        public static int CompareDepth(float a,float b)
        {
            // Comparator 0x3e87080 uses SUBSS, MULSS, CVTTSS2SI, not CompareTo.
            float difference=Float(a-b);return unchecked((int)Float(difference*10000f));
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static float Float(float value)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
        public static int Queue(Part part,int order,int queueOffset=0)
        {
            int b=BaseQueue(order);
            switch(part)
            {
                case Part.FaceSkin:case Part.FaceProp:case Part.HeadObj:return unchecked(1996-b);
                case Part.Head:case Part.Eye:case Part.Mayu:case Part.Jaw:return unchecked(1999-b);
                case Part.HeadWithoutHair:case Part.Hair:case Part.HairProp:return unchecked(1998-b);
                case Part.Prop:return 1999;
                case Part.Alpha:return unchecked(2300+4*order+queueOffset);
                default:return 2000;
            }
        }
        // Director.UpdateMiniModelRenderQueue calls (order,false,true): the
        // explicit zero-base flag makes these queues independent of actor order.
        // Complete original 0x33cf910 writes are native-mini-queue-writes.json.
        public static int MiniQueue(Part part)
        {
            switch(part)
            {
                case Part.FaceSkin:return 1994;
                case Part.Eye:return 1995;
                case Part.Mouth:return 1993;
                case Part.Hair:case Part.HairProp:case Part.FaceProp:return 1997;
                case Part.Mayu:return 1998;
                case Part.Attachment:return 0;
                default:return 2000;
            }
        }
        public void Clear(){_actors.Clear();_depth=Array.Empty<DepthInfo>();_cameraScopes.Clear();LastCamera=null;UpdateCount=0;}
        public void BeginCamera(Camera camera)
        {
            if(camera==null)return;
            int last=_cameraScopes.Count-1;
            if(last>=0&&_cameraScopes[last].camera==camera)
            {
                var scope=_cameraScopes[last];scope.callbacks++;_cameraScopes[last]=scope;
            }
            else _cameraScopes.Add(new CameraScope{camera=camera,callbacks=1});
            Update(camera);
        }
        public void EndCamera(Camera camera)
        {
            int last=_cameraScopes.Count-1;
            if(last<0)return;
            var scope=_cameraScopes[last];
            if(scope.camera!=camera)
            {
                Debug.LogError("[LiveModelRenderQueue] Camera scope ended out of order: "+camera);
                return;
            }
            if(--scope.callbacks>0){_cameraScopes[last]=scope;return;}
            _cameraScopes.RemoveAt(last);
            // SubmitRenderRequest may nest a mirror camera inside its receiver.
            // Restore that receiver's view-dependent queues before its remaining draws.
            if(last>0&&_cameraScopes[last-1].camera!=null)Update(_cameraScopes[last-1].camera);
        }
        public void Bind(IList<UmaContainerCharacter> actors)
        {
            Clear();if(actors==null)return;
            for(int i=0;i<actors.Count;i++)
            {
                var actor=actors[i];if(actor==null)continue;
                var binding=new ActorBinding{actor=actor};_actors.Add(binding);
                var renderers=actor.GetComponentsInChildren<Renderer>(true);
                bool hasHair=false;
                foreach(var r in renderers)if(r!=null&&r.name=="M_Hair")hasHair=true;
                var seen=new HashSet<Material>();
                foreach(var renderer in renderers)
                {
                    if(renderer==null)continue;
                    // Original RenderUtils.GetMaterialArray 0x51da430 calls both
                    // sharedMaterials and materials, then restores source names.
                    // Each actor/renderer must own its instances before queues vary.
                    var materials=renderer.materials;
                    foreach(var material in materials)
                    {
                        if(material==null||seen.Contains(material))continue;
                        material.name=material.name.Replace(" (Instance)","");
                        bool alpha=TryAlpha(renderer.name,out int alphaOffset,out Part secondPart);
                        bool recognized=TryPart(renderer.name,material,hasHair,out var part);
                        if(alpha)
                        {
                            recognized=true;
                            part=material.name.EndsWith("_Alpha"+alphaOffset,StringComparison.Ordinal)?Part.Alpha:secondPart;
                            // Original parts initializer's three AlphaMeshInfo
                            // lists assign Renderer.sortingOrder=1000.
                            renderer.sortingOrder=1000;
                        }
                        // Viewer compatibility workaround for the verified 1156
                        // balloon occlusion split. Native parts initialization
                        // writes 1000 only to alpha renderers; ordinary Live
                        // UpdateRenderQueue changes material queues, not these
                        // renderer orders. UpdateSortingOrder's zero writes are
                        // called by EventTimelineModelController, so they do not
                        // justify resetting ordinary Live renderers to zero.
                        // Retain the tested workaround pending a complete fix
                        // of the balloon draw/depth path; this is not a native
                        // all-renderer sorting-order rule.
                        renderer.sortingOrder=1000;
                        if(actor.IsMini&&renderer.name=="M_Face"){part=Part.FaceSkin;recognized=true;}
                        if(actor.IsMini&&renderer.name=="M_Mouth"){part=Part.Mouth;recognized=true;}
                        if(recognized)
                        {
                            seen.Add(material);binding.materials.Add(new MaterialBinding{material=material,renderer=renderer,part=part,rendererName=renderer.name,queueOffset=alphaOffset});
                        }
                        else if(renderer.name.StartsWith("M_",StringComparison.Ordinal))
                            binding.unverified.Add(renderer.name+"/"+material.name);
                    }
                }
                if(binding.unverified.Count>0)
                    Debug.LogWarning("[LiveModelRenderQueue] Unverified optional material categories for "+actor.name+": "+string.Join(", ",binding.unverified),actor);
            }
            int mainCount=0;foreach(var binding in _actors)if(!binding.actor.IsMini)mainCount++;
            _depth=new DepthInfo[mainCount];
        }
        // These authored renderer names are read by the original parts initializer
        // 0x5166910. Unknown/alpha/fade subclasses stay visible in the audit instead
        // of applying base opaque formulas to transparent or custom effect parts.
        public static bool TryPart(string rendererName,Material material,bool hasHair,out Part part)
        {
            part=default;
            // Original 0x5166910 tests these named groups with String.IndexOf,
            // so authored numeric suffixes (for example M_Body_Prop0) survive.
            if(rendererName.IndexOf("M_Body_Prop",StringComparison.Ordinal)>=0){part=Part.Body;return true;}
            if(rendererName.IndexOf("M_Hair_Prop",StringComparison.Ordinal)>=0){part=Part.HairProp;return true;}
            if(rendererName.IndexOf("M_Face_Prop",StringComparison.Ordinal)>=0){part=hasHair?Part.FaceProp:Part.HeadWithoutHair;return true;}
            if(rendererName.IndexOf("M_Body_Billboard",StringComparison.Ordinal)>=0){part=Part.BodyBillboard;return true;}
            if(rendererName.IndexOf("M_Hair_Billboard",StringComparison.Ordinal)>=0){part=Part.HairBillboard;return true;}
            switch(rendererName)
            {
                case "M_Face":
                    if(!hasHair)part=Part.HeadWithoutHair;
                    else part=material.name.Replace(" (Instance)","").EndsWith("_face",StringComparison.Ordinal)?Part.FaceSkin:Part.Head;
                    return true;
                case "M_Eye":part=Part.Eye;return true;
                case "M_Face_Prop":part=hasHair?Part.FaceProp:Part.HeadWithoutHair;return true;
                case "M_Hair":part=Part.Hair;return true;
                case "M_Hair_Prop":part=Part.HairProp;return true;
                case "M_Body":case "M_Body_Prop":part=Part.Body;return true;
                case "M_Tail":part=Part.Tail;return true;
                case "M_Mayu":case "M_Mayu_L":case "M_Mayu_R":part=Part.Mayu;return true;
                case "M_Jaw":part=Part.Jaw;return true;
                case "M_Attach":part=Part.Attachment;return true;
                case "M_Obj":part=Part.HeadObj;return true;
                case "M_Body_Billboard":part=Part.BodyBillboard;return true;
                case "M_Hair_Billboard":part=Part.HairBillboard;return true;
                default:return false;
            }
        }
        public static bool TryAlpha(string rendererName,out int offset,out Part secondPart)
        {
            offset=0;secondPart=Part.Body;
            foreach(string prefix in new[]{"M_Body_Alpha","M_Hair_Alpha","M_Attach_Alpha"})
                if(rendererName.StartsWith(prefix,StringComparison.Ordinal)&&int.TryParse(rendererName.Substring(prefix.Length),out offset))
                {
                    secondPart=prefix=="M_Hair_Alpha"?Part.Hair:prefix=="M_Attach_Alpha"?Part.Attachment:Part.Body;
                    return true;
                }
            return false;
        }
        public Observation[] Observe()
        {
            var rows=new List<Observation>();
            foreach(var actor in _actors)foreach(var binding in actor.materials)
                rows.Add(new Observation{actor=actor.actor!=null?actor.actor.name:null,actorId=actor.actor!=null?actor.actor.GetInstanceID():0,
                    renderer=binding.rendererName,rendererId=binding.renderer!=null?binding.renderer.GetInstanceID():0,part=binding.part.ToString(),
                    material=binding.material!=null?binding.material.name:null,materialId=binding.material!=null?binding.material.GetInstanceID():0,
                    queue=binding.material!=null?binding.material.renderQueue:0,queueOffset=binding.queueOffset});
            return rows.ToArray();
        }
        public void Update(Camera camera)
        {
            LastCamera=camera;UpdateCount++;
            int ordinal=0;
            for(int i=0;i<_actors.Count;i++)
            {
                var actor=_actors[i].actor;Vector3 position=actor!=null&&actor.HeadBone!=null?actor.HeadBone.transform.position:Vector3.zero;
                if(actor!=null&&actor.IsMini)
                {
                    foreach(var binding in _actors[i].materials)
                        if(binding.material!=null)binding.material.renderQueue=MiniQueue(binding.part);
                    continue;
                }
                _depth[ordinal++]=new DepthInfo{index=i,depth=camera!=null?camera.worldToCameraMatrix.MultiplyPoint(position).z:0};
            }
            Array.Sort(_depth,(a,b)=>CompareDepth(a.depth,b.depth));
            for(int order=0;order<_depth.Length;order++)
                foreach(var binding in _actors[_depth[order].index].materials)
                    if(binding.material!=null)binding.material.renderQueue=Queue(binding.part,order,binding.queueOffset);
        }
    }
}
