using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AnimateToUnity
{
    public sealed class AnRoot : MonoBehaviour
    {
        public AnRootParameter _parameter;
        public AnMeshParameterGroup _meshParameterGroup;
        public bool _ignoreMargin,_isWriteAlphaChannel;
        public bool _initializeFlag;
        public bool IsInitialized{get;private set;}
        public readonly List<AnMotionInstance> Motions=new List<AnMotionInstance>();
        public readonly List<AnObjectInstance> Objects=new List<AnObjectInstance>();
        public AnMotionInstance RootMotion{get;private set;}
        public readonly AnMotionClock.Context Clock=new AnMotionClock.Context();
        public Func<string,Font> FontResolver;
        public bool Paused;
        int sortOrder;
        int sortInterval=1;
        public int SortInterval=>sortInterval;
        public int SortOrderCount=>sortOrder;
        int stencilCount=-1;
        Vector2 viewportSize;
        public void SetViewportSize(Vector2 size)=>viewportSize=size;
        internal Vector3 GetAnchorOffset(AnObjectInstance node)
        {
            int anchor=(int)node.Parameter._placeAnchorType;
            if(anchor==0||viewportSize==Vector2.zero)return Vector3.zero;
            // Native AnRootParameter._Initialize prefers baseScreenFixSize;
            // _UpdateBasePlaceOffset / _UpdatePlaceOffset retain the authored
            // distance to the selected edge. Live forces anchor scale to Fix.
            var basis=_parameter._baseScreenFixSize;
            if(basis.x<=0f||basis.y<=0f)basis=_parameter._baseScreenSize;
            var delta=(viewportSize-basis)*.5f;
            int column=(anchor-1)%3,row=(anchor-1)/3;
            var shift=new Vector3((column-1)*delta.x,(1-row)*delta.y,0f);
            return node.Parent.Transform.InverseTransformVector(transform.TransformVector(shift));
        }
        public int AllocateStencil(){stencilCount=stencilCount==-1?0:stencilCount+3;return stencilCount;}

        public void Initialize()
        {
            if(IsInitialized)return;
            if(_parameter==null)throw new InvalidOperationException("A2U root parameter missing: "+name);
            gameObject.layer=LayerMask.NameToLayer(_parameter._layerName);
            Clock.SourceFrameTime=1f/_parameter._baseFrameRate;
            // The native text table reserves room for shadow/outline sorting.
            foreach(var motionParameter in _parameter._motionParameterGroup._motionParameterList)
                foreach(var text in motionParameter._textParamList)
                    if(text._textMeshType!=AnTextMeshTypes.Normal)
                        sortInterval=text._textMeshType==AnTextMeshTypes.Shadow?2:3;
            var parameter=_parameter._motionParameterGroup._motionParameterList.First(x=>x._id==_parameter._rootMotionID);
            RootMotion=CreateMotion(parameter,transform,null);
            foreach(var node in Objects)node.SetSortOrder(sortOrder-node.SortOrderIndex+_parameter._sortOffset+node.SortOffset);
            foreach(var node in Objects)node.Text?.BindGradation(node.Parent.Transform);
            _initializeFlag=true;IsInitialized=true;
            Evaluate(0f);
            _initializeFlag=false;
        }
        AnMotionInstance CreateMotion(AnMotionParameter parameter,Transform target,AnObjectInstance parent)
        {
            var motion=new AnMotionInstance(this,parameter,target,parent);Motions.Add(motion);
            var parameters=new List<AnObjectParameterBase>();
            parameters.AddRange(parameter._objectParamList);parameters.AddRange(parameter._planeParamList);parameters.AddRange(parameter._textParamList);
            parameters.Sort((a,b)=>a._objectIndex-b._objectIndex);
            foreach(var p in parameters)
            {
                string prefix=p is AnObjectParameter?"OBJ_":p is AnTextParameter?"TXT_":"PLN_";
                sortOrder+=p is AnObjectParameter?(p._collisionType==AnCollisionTypes.None?0:1):sortInterval;
                var node=new AnObjectInstance(this,p,motion,new GameObject(prefix+p._objectName).transform,sortOrder);
                motion.Objects.Add(node);Objects.Add(node);
                if(p is AnObjectParameter obj&&!string.IsNullOrEmpty(obj._childMotionID))
                {
                    var child=_parameter._motionParameterGroup._motionParameterList.FirstOrDefault(x=>x._id==obj._childMotionID);
                    if(child!=null)
                    {
                        var childTarget=new GameObject("MOT_"+child._name).transform;childTarget.gameObject.layer=gameObject.layer;childTarget.SetParent(node.OffsetTransform,false);
                        node.ChildMotion=CreateMotion(child,childTarget,node);
                    }
                }
            }
            motion.Play(parameter._labelParamList[0]._name);
            return motion;
        }
        public AnMeshInfoParameter FindMesh(string textureName,out AnMeshInfoParameterGroup group)
        {
            foreach(var mesh in _meshParameterGroup._meshParameterList)
                foreach(var candidate in mesh._meshParameterGroupList)
                    foreach(var info in candidate._meshInfoParameterList)
                        if(info._textureName==textureName){group=candidate;return info;}
            group=null;return null;
        }
        public void Evaluate(float deltaTime)
        {
            if(!IsInitialized)return;
            Clock.DeltaTime=Paused?0f:deltaTime;
            RootMotion.Evaluate(Color.white,Color.clear,true);
            foreach(var node in Objects)node.Text?.UpdateGradation();
            Clock.CurrentTime+=Clock.DeltaTime;
        }
        public void SetCurrentTimeAll(float time)=>AnMotionClock.SetCurrentTimeAll(Motions.Select(x=>x.Clock),time);
        public AnMotionInstance FindMotion(string name)=>Motions.FirstOrDefault(x=>x.Transform.name==name||x.Parameter._name==name);
        public AnObjectInstance FindObject(string name,Transform searchRoot=null)=>Objects.FirstOrDefault(x=>MatchesObject(x,name)&&(searchRoot==null||x.Transform.IsChildOf(searchRoot)));
        public static bool MatchesObject(AnObjectInstance node,string name)
        {
            if(name.IndexOf('/')<0)return node.Transform.name==name||node.Parameter._objectName==name;
            var segments=name.Split('/');var current=node.Transform;
            for(int i=segments.Length-1;i>=0;i--)
            {
                if(current==null||current.name!=segments[i])return false;
                current=current.parent;
            }
            return true;
        }
        void OnDestroy(){foreach(var node in Objects)node.Dispose();}
    }

    public sealed class AnMotionInstance
    {
        public readonly AnRoot Root;
        public readonly AnMotionParameter Parameter;
        public readonly Transform Transform;
        public readonly AnObjectInstance Parent;
        public readonly AnMotionClock Clock;
        public readonly List<AnObjectInstance> Objects=new List<AnObjectInstance>();
        public Color MultiplyColor=Color.white,ColorOffset=Color.clear;
        public bool Visible=true;
        public bool VisibleInHierarchy{get;private set;}
        Color currentColor=Color.white,currentColorOffset=Color.clear;
        public int? StencilCount;
        readonly Dictionary<string,System.Action[]> actions=new Dictionary<string,System.Action[]>();
        readonly Dictionary<string,System.Action[]> normalActions=new Dictionary<string,System.Action[]>();
        readonly HashSet<AnObjectControlInfoParameter> executedControls=new HashSet<AnObjectControlInfoParameter>();
        public AnMotionInstance(AnRoot root,AnMotionParameter parameter,Transform transform,AnObjectInstance parent)
        {
            Root=root;Parameter=parameter;Transform=transform;Parent=parent;Clock=new AnMotionClock(parameter,root.Clock);
            Clock.SetActions=(clock,label)=>{executedControls.Clear();SelectActions(label);};
        }
        void SelectActions(string label)
        {
            normalActions.TryGetValue(label,out var normal);
            Clock.Start=normal?[0];Clock.Loop=normal?[1];Clock.Finish=normal?[2];
            Clock.PendingStart=Clock.Start!=null;Clock.PendingFinish=Clock.Finish!=null;
            actions.TryGetValue(label,out var callbacks);
            Clock.FlStart=callbacks?[0];Clock.FlLoop=callbacks?[1];Clock.FlFinish=callbacks?[2];
            Clock.PendingFlStart=Clock.FlStart!=null;Clock.PendingFlFinish=Clock.FlFinish!=null;
        }
        public void AddAction(string label,int type,System.Action callback)
        {
            if(!actions.TryGetValue(label,out var callbacks)){callbacks=new System.Action[3];actions.Add(label,callbacks);}
            callbacks[type]+=callback;
            if(Clock.LabelName==label)SelectActions(label);
        }
        public void SetAction(string label,int type,System.Action callback)
        {
            if(!normalActions.TryGetValue(label,out var callbacks)){callbacks=new System.Action[3];normalActions.Add(label,callbacks);}
            callbacks[type]=callback;
            if(Clock.LabelName==label)SelectActions(label);
        }
        public void RemoveAction(string label,int type)
        {
            if(normalActions.TryGetValue(label,out var callbacks))callbacks[type]=null;
            if(Clock.LabelName==label)SelectActions(label);
        }
        public bool Play(string label,float offset=0f,bool pause=false)
        {
            var parameter=Parameter._labelParamList.FirstOrDefault(x=>x._name==label);
            if(parameter==null)return false;
            PlayAtTime(AnFloat.Round(parameter._timeRange.x+offset),pause,true);
            return true;
        }
        public void PlayAtTime(float time,bool pause=false,bool resetByStartLabel=false)
        {
            var label=Parameter._labelParamList.FirstOrDefault(x=>x._timeRange.x<=time&&time<x._timeRange.y)??Parameter._labelParamList[0];
            Clock.Play(label._name,AnFloat.Round(time-label._timeRange.x),pause);
            bool force=resetByStartLabel&&time<=Parameter._labelParamList[0]._timeRange.x;
            foreach(var node in Objects)node.ResetTime(force);
            if(Root.IsInitialized&&!Root._initializeFlag&&(Parent==null||Parent.VisibleInHierarchy))
            {
                // Native _SetMotionPlayBase calls virtual _UpdateFirst, including
                // label actions. A paused Live clock still dispatches these actions;
                // it only freezes elapsed time. Skipping them loses Champions SE
                // and particles because that controller has no automatic update.
                float delta=Root.Clock.DeltaTime;
                Root.Clock.DeltaTime=0f;
                try { Evaluate(Parent!=null?Parent.State.CurrentColor:Color.white,Parent!=null?Parent.State.CurrentColorOffset:Color.clear,true); }
                finally { Root.Clock.DeltaTime=delta; }
            }
        }
        public void ResetTime(AnMotionResetModeTypes mode,bool force=false)
        {
            if(mode==AnMotionResetModeTypes.None)return;
            if(Parent!=null)
            {
                float time=Parent.Parent.Clock.CurrentTime;
                force=force||Parent.IsResetTime||time<=AnFloat.Round(Parent.Parameter._timeRange.x+.0001f)||time>=AnFloat.Round(Parent.Parameter._timeRange.y-.0001f);
            }
            if(force)
            {
                float time=mode==AnMotionResetModeTypes.ResetLabel?Clock.Begin:-.00001f;
                var label=Parameter._labelParamList.FirstOrDefault(x=>x._timeRange.x<=time&&time<x._timeRange.y)??Parameter._labelParamList[0];
                Clock.Play(label._name,time-label._timeRange.x);
            }
            Clock.State=0;
            foreach(var node in Objects)node.ResetTime(force);
        }
        public void Evaluate(Color parentColor,Color parentOffset,bool parentVisible,bool updateState=true)
        {
            VisibleInHierarchy=Root._initializeFlag||parentVisible&&Visible;
            if(VisibleInHierarchy){currentColor=MultiplyColor*parentColor;currentColorOffset=ColorOffset+parentOffset;}
            if(updateState)Clock.UpdateState();
            Clock.UpdateTime();
            foreach(var node in Objects)node.Evaluate(currentColor,currentColorOffset,VisibleInHierarchy,updateState);
            var label=Parameter._labelParamList[Clock.LabelIndex];
            foreach(var control in label._objectControlInfoParamList)
                if(control._startTime<=Clock.ObjectTime&&!executedControls.Contains(control))
                {
                    var target=Objects.FirstOrDefault(x=>x.Parameter._objectName==control._targetName)?.ChildMotion;
                    if(target!=null)
                    {
                        string targetLabel=control._targetLabel;
                        if(string.IsNullOrEmpty(targetLabel))targetLabel=target.Parameter._labelParamList[0]._name;
                        var targetParameter=target.Parameter._labelParamList.FirstOrDefault(x=>x._name==targetLabel)??target.Parameter._labelParamList[0];
                        target.PlayAtTime(AnFloat.Round(targetParameter._timeRange.x+control._targetTime),control._targetIsStop);
                    }
                    executedControls.Add(control);
                }
            if(VisibleInHierarchy&&(updateState||Root._initializeFlag||AnFloat.Round(currentColor.a+currentColorOffset.a)>.001f))
                Clock.PreviousTime=Clock.CurrentTime;
        }
    }

    public sealed class AnObjectInstance : IDisposable
    {
        public readonly AnRoot Root;
        public readonly AnObjectParameterBase Parameter;
        public readonly AnMotionInstance Parent;
        public readonly AnObjectState State;
        public readonly Transform Transform,OffsetTransform;
        public readonly AnPlaneMesh Plane;
        public readonly AnTextMesh Text;
        public readonly int SortOrderIndex,LocalStencilRef,StencilRef,StencilCompare;
        public readonly AnBlendModeTypes BlendMode;
        public readonly AnObjectTypes ObjectType;
        public AnMotionInstance ChildMotion;
        public bool Visible{get;private set;}
        public bool VisibleInHierarchy{get;private set;}
        bool wasInTimeRange;
        internal bool IsResetTime{get;private set;}
        public AnObjectInstance(AnRoot root,AnObjectParameterBase parameter,AnMotionInstance parent,Transform transform,int order)
        {
            Root=root;Parameter=parameter;Parent=parent;Transform=transform;State=new AnObjectState(parameter);
            Transform.SetParent(parent.Transform,false);Transform.gameObject.layer=root.gameObject.layer;
            Transform.localPosition=parameter._position;Transform.localRotation=Quaternion.Euler(parameter._rotate);Transform.localScale=parameter._scale;
            OffsetTransform=Transform;
            if(parameter._positionOffset!=Vector3.zero||parameter._positionOffsetKeyParamList.Count!=0)
            {OffsetTransform=new GameObject("offset").transform;OffsetTransform.SetParent(Transform,false);OffsetTransform.localPosition=parameter._positionOffset;OffsetTransform.gameObject.layer=Transform.gameObject.layer;}
            SortOrderIndex=order;
            SortOffset=parent.Parent!=null&&parent.Parent.SortOffset!=0?parent.Parent.SortOffset:parameter._sortOffset;
            if(parameter._stencilRef!=0)
            {
                if(!parent.StencilCount.HasValue)parent.StencilCount=root.AllocateStencil();
                LocalStencilRef=parent.StencilCount.Value+parameter._stencilRef;
            }
            if(parent.Parent!=null&&parent.Parent.LocalStencilRef!=0)LocalStencilRef=parent.Parent.LocalStencilRef;
            StencilRef=root._parameter._stencilRefOffset+(LocalStencilRef>0?LocalStencilRef:0);
            BlendMode=parent.Parent!=null&&parent.Parent.BlendMode!=AnBlendModeTypes.Normal?parent.Parent.BlendMode:parameter._blendModeType;
            ObjectType=parent.Parent!=null&&(int)parent.Parent.ObjectType>=4&&(int)parent.Parent.ObjectType<=10?parent.Parent.ObjectType:parameter._objectType;
            int compare=StencilRef!=root._parameter._stencilRefOffset?3:(int)root._parameter._stencilCompareFunc;
            StencilCompare=ObjectType==AnObjectTypes.StencilMask||ObjectType==AnObjectTypes.StencilAlphaMask?8:compare;
            if(parameter is AnPlaneParameter plane)Plane=new AnPlaneMesh(root,plane,OffsetTransform,order+SortOffset,BlendMode,ObjectType,StencilRef,compare);
            if(parameter is AnTextParameter text)Text=new AnTextMesh(root,text,OffsetTransform,order+SortOffset,BlendMode,StencilRef,compare);
        }
        public int SortOffset{get;}
        public void SetSortOrder(int order){if(Plane!=null)Plane.Renderer.sortingOrder=order;Text?.SetSortOrder(order);}
        public void ResetTime(bool force=false)
        {
            State.ResetTime();IsResetTime=force;
            if(Parameter is AnObjectParameter obj)ChildMotion?.ResetTime(obj._motionResetModeType);
            IsResetTime=false;
        }
        public void Evaluate(Color color,Color offset,bool parentVisible,bool updateState=true)
        {
            bool inRange=State.InTimeRange(Parent.Clock);
            if(inRange&&!wasInTimeRange&&!Root._initializeFlag)
            {ResetTime(true);wasInTimeRange=true;}
            else if(!inRange)wasInTimeRange=false;
            VisibleInHierarchy=Root._initializeFlag||parentVisible&&inRange;
            if(VisibleInHierarchy)State.EvaluateColor(Parent.Clock,color,offset);
            Visible=VisibleInHierarchy&&(Root._initializeFlag||State.VisibleByAlpha);
            if(Visible)
            {
                State.EvaluateTransform(Parent.Clock);
                Transform.localPosition=State.LocalPosition+Root.GetAnchorOffset(this);Transform.localRotation=Quaternion.Euler(State.Rotate);
                Transform.localScale=Plane==null?State.Scale:Vector3.one;
                // Native mesh rendering uses the offset child (AnObjectBase +0x158).
                // Baking the pivot into vertices does not replace that child's translation.
                if(OffsetTransform!=Transform)OffsetTransform.localPosition=State.PositionOffset;
            }
            Plane?.Update(State,Parent.Clock,Visible);Text?.Update(State,Visible);
            ChildMotion?.Evaluate(State.CurrentColor,State.CurrentColorOffset,VisibleInHierarchy,updateState);
        }
        public void Dispose(){Plane?.Dispose();Text?.Dispose();}
    }
}
