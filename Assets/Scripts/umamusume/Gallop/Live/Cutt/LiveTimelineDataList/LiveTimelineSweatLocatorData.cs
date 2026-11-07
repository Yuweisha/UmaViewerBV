using System;
using System.Collections.Generic;
using UnityEngine;
namespace Gallop.Live.Cutt
{
    [Serializable] public class LiveTimelineKeySweatLocatorData : LiveTimelineKeyWithInterpolate
    {
        public enum LocatorType { Local=0, World=1 }
        [Serializable] public struct LocatorInfo
        {public bool isVisible;public Vector3 offset,offsetAngle;public LocatorType offsetType;}
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.SweatLocator;
        public int owner;public float alpha=1f;public int randomVisibleCount;
        public bool locator0_isVisible;public Vector3 locator0_offset,locator0_offsetAngle;public LocatorType locator0_offsetType;
        public bool locator1_isVisible;public Vector3 locator1_offset,locator1_offsetAngle;public LocatorType locator1_offsetType;
        public bool locator2_isVisible;public Vector3 locator2_offset,locator2_offsetAngle;public LocatorType locator2_offsetType;
        public bool locator3_isVisible;public Vector3 locator3_offset,locator3_offsetAngle;public LocatorType locator3_offsetType;
        public bool locator4_isVisible;public Vector3 locator4_offset,locator4_offsetAngle;public LocatorType locator4_offsetType;
        public LocatorInfo Locator(int i)
        {
            switch(i)
            {
                case 0:return new LocatorInfo{isVisible=locator0_isVisible,offset=locator0_offset,offsetAngle=locator0_offsetAngle,offsetType=locator0_offsetType};
                case 1:return new LocatorInfo{isVisible=locator1_isVisible,offset=locator1_offset,offsetAngle=locator1_offsetAngle,offsetType=locator1_offsetType};
                case 2:return new LocatorInfo{isVisible=locator2_isVisible,offset=locator2_offset,offsetAngle=locator2_offsetAngle,offsetType=locator2_offsetType};
                case 3:return new LocatorInfo{isVisible=locator3_isVisible,offset=locator3_offset,offsetAngle=locator3_offsetAngle,offsetType=locator3_offsetType};
                case 4:return new LocatorInfo{isVisible=locator4_isVisible,offset=locator4_offset,offsetAngle=locator4_offsetAngle,offsetType=locator4_offsetType};
                default:throw new IndexOutOfRangeException();
            }
        }
    }
    [Serializable] public class LiveTimelineKeySweatLocatorDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeySweatLocatorData>{}
    [Serializable] public class LiveTimelineSweatLocatorData:ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeySweatLocatorDataList keys=new LiveTimelineKeySweatLocatorDataList();
        public List<int> randomVisibleIndex=new List<int>();
        public LiveTimelineSweatLocatorData():base("SweatLocator"){}
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public enum SweatLocatorIndex { Sweat1=0, Sweat2=1, Sweat3=2, Sweat4=3, Sweat5=4, maxNum=5 }
    public class SweatLocatorUpdateInfo
    {
        public int owner;public float alpha;
        public LiveTimelineKeySweatLocatorData.LocatorInfo[] locatorInfo;
        public SweatLocatorUpdateInfo(int arrayNum){locatorInfo=new LiveTimelineKeySweatLocatorData.LocatorInfo[arrayNum];}
    }
    public delegate void SweatLocatorUpdateInfoDelegate(ref SweatLocatorUpdateInfo info);
    public partial class LiveTimelineWorkSheet
    {[SerializeField]public List<LiveTimelineSweatLocatorData> sweatLocatorList;}
}

