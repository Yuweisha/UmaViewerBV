using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    public sealed class EventCharaFKAnimationGroup:ScriptableObject
    {
        [Serializable] public struct FKData{public int DressId;public string[] StateNameArray;}
        [Serializable] public struct FKGroup{public int CharaId;public FKData[] FKDataArray;}
        public FKGroup[] FKGroupArray;

        public sealed class Runtime
        {
            public readonly FKData[] Data;
            Runtime(FKData[] data){Data=data;}
            public static Runtime Create(EventCharaFKAnimationGroup asset,int character,int dress)
            {
                var data=new List<FKData>();
                foreach(var group in asset.FKGroupArray)
                    if(group.CharaId==0||group.CharaId==character)
                        foreach(var row in group.FKDataArray)if(row.DressId==0||row.DressId==dress)data.Add(row);
                return new Runtime(data.ToArray());
            }
            public bool IsFKMotion(string motion)
            {foreach(var row in Data)if(Array.IndexOf(row.StateNameArray,motion)>=0)return true;return false;}
        }
    }
}
