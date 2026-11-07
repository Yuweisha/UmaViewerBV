using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    // The zero-duration animated ear path of DrivenKeyComponent.UpdateEarWeight.
    // WeightGroupBase stores current, target and previous banks, each17*6 floats.
    public sealed class LiveMiniEarWeightRuntime
    {
        readonly Transform[,] locators=new Transform[2,17];
        public readonly float[] CommonWeights=new float[17*6*3];
        public readonly List<EarType> BlendEarTypes=new List<EarType>();
        public LiveMiniEarWeightRuntime(Transform locator)
        {
            var nodes=locator.GetComponentsInChildren<Transform>(true).ToDictionary(node=>node.name);
            for(int group=0;group<2;group++)for(int ear=0;ear<17;ear++)nodes.TryGetValue((group==0?"Ear_R__":"Ear_L__")+((EarType)ear).ToString(),out locators[group,ear]);
            for(int i=0;i<6;i++){CommonWeights[i]=1;CommonWeights[17*6*2+i]=1;}
        }
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
        public void UpdateAnimation(DrivenKeyAnimation animation)
        {
            BlendEarTypes.Clear();BlendEarTypes.Add(EarType.Base);if(animation==null)return;
            // Native animationEarType uses the saved array length, not count.
            for(int group=0;group<animation.animationEarType.Length;group++)foreach(var ear in animation.animationEarType[group].faceType)
            {
                var node=locators[group,(int)ear];if(node==null)continue;var value=node.localPosition;int start=(int)ear*6+group*3;float x=F(value.x*-100f),y=F(value.y*100f),z=F(value.z*100f);CommonWeights[start]=x;CommonWeights[start+1]=y;CommonWeights[start+2]=z;if(F(F(x+y)+z)>Mathf.Epsilon&&!BlendEarTypes.Contains(ear))BlendEarTypes.Add(ear);
            }
        }
    }
}
