using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    // Original3eda4d0..3edacdD. The host owns installation and phase calls;
    // registration callbacks do not create a missing updater implicitly.
    public sealed class ConstraintUpdater : MonoBehaviour
    {
        public static ConstraintUpdater Instance {get;private set;}
        readonly List<ConstraintBehaviour> _executeList=new List<ConstraintBehaviour>();
        readonly HashSet<ConstraintBehaviour> _executeSet=new HashSet<ConstraintBehaviour>();
        readonly Queue<ConstraintBehaviour> _registerQueue=new Queue<ConstraintBehaviour>();
        readonly Queue<ConstraintBehaviour> _unregisterQueue=new Queue<ConstraintBehaviour>();
        bool _isBusy;
        void Awake(){Instance=this;}
        void OnDestroy(){if(Instance==this)Instance=null;}
        public void AddItem(ConstraintBehaviour item)=>_registerQueue.Enqueue(item);
        public void RemoveItem(ConstraintBehaviour item)=>_unregisterQueue.Enqueue(item);
        void RegisterItems(){while(_registerQueue.Count>0){var item=_registerQueue.Dequeue();if(_executeSet.Add(item))_executeList.Add(item);}}
        void UnregisterItems(){while(_unregisterQueue.Count>0){var item=_unregisterQueue.Dequeue();if(_executeSet.Remove(item))_executeList.Remove(item);}}
        void UpdateDelayedArray(){_isBusy=true;RegisterItems();UnregisterItems();_isBusy=false;}
        public void AlterLateUpdatePre()
        {
            UpdateDelayedArray();int count=_executeList.Count;
            for(int i=0;i<count;i++){var item=_executeList[i];if(item.gameObject.activeSelf&&item.enabled)item.AlterLateUpdatePre();}
        }
        public void AlterLateUpdateBeforeModel()
        {
            int count=_executeList.Count;
            for(int i=0;i<count;i++){var item=_executeList[i];if(item.gameObject.activeSelf&&item.enabled)item.AlterLateUpdateBeforeModel();}
        }
    }
}
