using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    [Serializable] public sealed class StageObjectSelect
    {
        public enum Condition {Random=0,Direct=1}
        [SerializeField] string _prefabName;
        [SerializeField] Condition _condition;
        [SerializeField] int _selectIndex;
        [SerializeField] GameObject[] _stageObjectArray;
        bool _isEnabled;
        public string PrefabName=>_prefabName;
        public bool IsEnabled=>_isEnabled;
        public GameObject SelectObject=>_stageObjectArray[_selectIndex];
        public void Initialize(bool isSetExternal,Condition condition,int selectIndex)
        {Initialize(isSetExternal,condition,selectIndex,LiveRandomBoundary.RangeInt);}
        public void Initialize(bool isSetExternal,Condition condition,int selectIndex,Func<int,int,int> random)
        {
            _isEnabled=false;
            if(string.IsNullOrEmpty(_prefabName)||_stageObjectArray==null||_stageObjectArray.Length==0)return;
            if(isSetExternal){_condition=condition;_selectIndex=selectIndex;}
            switch(_condition)
            {
                case Condition.Random:_selectIndex=random(0,_stageObjectArray.Length);break;
                case Condition.Direct:break;
                default:_selectIndex=0;break;
            }
            if(_selectIndex<0||_selectIndex>=_stageObjectArray.Length)_selectIndex=0;
            _isEnabled=_stageObjectArray[_selectIndex]!=null;
        }
    }

    public static class LiveStageObjectSelection
    {
        // Native 4ead880: the settings getter is re-read for each non-null
        // selection. Index -1 requests Random; other negative values do too.
        public static void Initialize(ref GameObject[] objects,StageObjectSelect[] selects,
            Func<IDictionary<int,int>> settings,Func<int,int,int> random)
        {
            int originalCount=objects.Length,total=originalCount;
            for(int i=0;i<selects.Length;i++)
            {
                var selection=selects[i];if(selection==null)continue;
                bool found=settings().TryGetValue(i,out int index);
                selection.Initialize(found,found&&index>=0?StageObjectSelect.Condition.Direct:StageObjectSelect.Condition.Random,index,random);
                if(selection.IsEnabled)total++;
            }
            if(total==originalCount)return;
            Array.Resize(ref objects,total);
            foreach(var selection in selects)
                if(selection!=null&&selection.IsEnabled)objects[originalCount++]=selection.SelectObject;
        }
    }

    public partial class StageController
    {
        void InitializeStageObjectSelect()
        {
            var objects=_stageObjects.ToArray();
            LiveStageObjectSelection.Initialize(ref objects,_stageObjectSelects,
                ()=>Director.instance.StageObjectSelectTargetDictionary,LiveRandomBoundary.RangeInt);
            // Keep the Viewer list object, while native's authored array is
            // resized only when enabled selections are appended.
            if(objects.Length!=_stageObjects.Count)_stageObjects.AddRange(
                new ArraySegment<GameObject>(objects,_stageObjects.Count,objects.Length-_stageObjects.Count));
        }
    }
}
