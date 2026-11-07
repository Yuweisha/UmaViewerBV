using System.Collections.Generic;

using UnityEngine;

namespace AnimateToUnity
{
    public sealed class AnObjectState
    {
        public readonly AnObjectParameterBase Parameter;
        public Vector3 Position,PositionOffset,Rotate,Scale,PlaceOffset,PlaceScale=Vector3.one;
        public Vector2 Shear;
        public Color MultiplyColor=Color.white,ColorOffset=Color.clear,CurrentColor,CurrentColorOffset;
        public bool VisibleByAlpha;
        public float LocalDepth,FixedLocalDepth;
        readonly int[][] indices={new int[3],new int[3],new int[3],new int[3],new int[2],new int[4],new int[4]};
        static float F(float value)=>AnFloat.Round(value);
        public AnObjectState(AnObjectParameterBase parameter)
        {
            Parameter=parameter;
            foreach(var group in new[]{parameter._positionKeyParamList,parameter._positionOffsetKeyParamList,parameter._rotateKeyParamList,parameter._scaleKeyParamList,parameter._shearKeyParamList,parameter._colorKeyParamList,parameter._colorOffsetKeyParamList,parameter._blurKeyParamList})
                foreach(var curve in group)curve.Initialize();
        }
        float Value(List<AnKeyParameter> curves,int group,int axis,float original,AnMotionClock clock,bool stabilize=false)
        {
            if(curves.Count<=axis||curves[axis]._keyCount==0)return original;
            return curves[axis].GetValue(original,clock.CurrentTime,clock.PreviousTime,clock.FixObjectTime,ref indices[group][axis],stabilize);
        }
        public void EvaluateTransform(AnMotionClock clock)
        {
            var p=Parameter;Position=new Vector3(F(p._position.x+PlaceOffset.x),F(p._position.y+PlaceOffset.y),F(F(LocalDepth+PlaceOffset.z)+p._position.z));
            PositionOffset=p._positionOffset;Rotate=p._rotate;Scale=new Vector3(F(p._scale.x*PlaceScale.x),F(p._scale.y*PlaceScale.y),F(p._scale.z*PlaceScale.z));Shear=p._shear;
            for(int axis=0;axis<3;axis++)
            {
                if(p._positionKeyParamList.Count>axis&&p._positionKeyParamList[axis]._keyCount>0)Position[axis]=Value(p._positionKeyParamList,0,axis,p._position[axis],clock);
                PositionOffset[axis]=Value(p._positionOffsetKeyParamList,1,axis,p._positionOffset[axis],clock);
                Rotate[axis]=Value(p._rotateKeyParamList,2,axis,p._rotate[axis],clock,p._isStabilizeRotation);
                if(p._scaleKeyParamList.Count>axis&&p._scaleKeyParamList[axis]._keyCount>0)Scale[axis]=Value(p._scaleKeyParamList,3,axis,p._scale[axis],clock);
            }
            for(int axis=0;axis<2;axis++)Shear[axis]=Value(p._shearKeyParamList,4,axis,p._shear[axis],clock,p._isStabilizeRotation);
        }
        public void EvaluateColor(AnMotionClock clock,Color parentColor,Color parentOffset)
        {
            var p=Parameter;CurrentColor=p._color;CurrentColorOffset=p._colorOffset;
            for(int axis=0;axis<4;axis++)
            {
                CurrentColor[axis]=Value(p._colorKeyParamList,5,axis,p._color[axis],clock);
                CurrentColorOffset[axis]=Value(p._colorOffsetKeyParamList,6,axis,p._colorOffset[axis],clock);
                CurrentColor[axis]=F(F(CurrentColor[axis]*MultiplyColor[axis])*parentColor[axis]);
                CurrentColorOffset[axis]=F(F(F(CurrentColorOffset[axis]+ColorOffset[axis])*parentColor[axis])+parentOffset[axis]);
            }
            VisibleByAlpha=F(CurrentColor.a+CurrentColorOffset.a)>.001f;
        }
        public bool InTimeRange(AnMotionClock clock)=>Parameter._timeRange.x<=clock.ObjectTime&&clock.ObjectTime<Parameter._timeRange.y;
        public void ResetTime(){foreach(var group in indices)System.Array.Clear(group,0,group.Length);}
        public Vector3 LocalPosition=>new Vector3(F(Position.x+PlaceOffset.x),F(Position.y+PlaceOffset.y),F(F(FixedLocalDepth+Position.z)+PlaceOffset.z));
    }
}
