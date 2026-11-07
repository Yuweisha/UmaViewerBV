using System.Collections.Generic;
using Gallop.Live;
using UnityEngine;
namespace Gallop.Live.Cutt
{
    public partial class LiveTimelineControl
    {
        public event SweatLocatorUpdateInfoDelegate OnUpdateSweatLocator;
        SweatLocatorUpdateInfo sweatLocatorUpdate=new SweatLocatorUpdateInfo(5);
        readonly List<int> sweatLocatorCandidates=new List<int>(5);
        private void AlterUpdate_SweatLocator(LiveTimelineWorkSheet sheet,int frame)
        {
            // Original subscriber gate precedes sheet/list access.
            if(OnUpdateSweatLocator==null)return;
            foreach(var group in sheet.sweatLocatorList)
            {
                var keys=group.keys;
                if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))continue;
                // Original uses force=true rather than the global optional cache flag.
                var selected=keys.FindKeyCached(frame,true);
                var a=selected.key as LiveTimelineKeySweatLocatorData;
                if(a==null)continue;
                var b=keys.At(selected.index+1)as LiveTimelineKeySweatLocatorData;
                bool interpolate=b!=null&&b.IsInterpolateKey();
                float rate=interpolate?CalculateInterpolationValue(a,b,frame):0f;
                sweatLocatorUpdate.owner=a.owner;
                sweatLocatorUpdate.alpha=interpolate?CySpringWindParam.NativeLerp(a.alpha,b.alpha,rate):a.alpha;
                for(int i=0;i<5;i++)
                {
                    var part=a.Locator(i);
                    if(interpolate)
                    {
                        var next=b.Locator(i);
                        for(int axis=0;axis<3;axis++)
                        {
                            part.offset[axis]=CySpringWindParam.NativeLerp(part.offset[axis],next.offset[axis],rate);
                            part.offsetAngle[axis]=CySpringWindParam.NativeLerp(part.offsetAngle[axis],next.offsetAngle[axis],rate);
                        }
                    }
                    sweatLocatorUpdate.locatorInfo[i]=part;
                }
                var visible=group.randomVisibleIndex;
                if(visible.Count<a.randomVisibleCount)
                {
                    sweatLocatorCandidates.Clear();for(int i=0;i<5;i++)sweatLocatorCandidates.Add(i);
                    do
                    {
                        int pick=LiveRandomBoundary.RangeInt(0,sweatLocatorCandidates.Count);
                        int value=sweatLocatorCandidates[pick];
                        if(!visible.Contains(value))visible.Add(value);
                        sweatLocatorCandidates.RemoveAt(pick);
                    }while(visible.Count<a.randomVisibleCount&&sweatLocatorCandidates.Count>0);
                }
                else if(a.randomVisibleCount<visible.Count)
                    do{visible.RemoveAt(LiveRandomBoundary.RangeInt(0,visible.Count));}while(a.randomVisibleCount<visible.Count);
                if(visible.Count>0)
                {
                    for(int i=0;i<5;i++)
                    {
                        var part=sweatLocatorUpdate.locatorInfo[i];part.isVisible=false;
                        part.offset=part.offsetAngle=Vector3.zero;sweatLocatorUpdate.locatorInfo[i]=part;
                    }
                    foreach(int i in visible){var part=sweatLocatorUpdate.locatorInfo[i];part.isVisible=true;sweatLocatorUpdate.locatorInfo[i]=part;}
                }
                // Reference object identity remains stable, as in native owner+ae0.
                OnUpdateSweatLocator(ref sweatLocatorUpdate);
            }
        }
    }
}

