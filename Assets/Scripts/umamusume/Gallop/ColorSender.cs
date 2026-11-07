using System.Collections.Generic;
using Gallop.Live;
using UnityEngine;
using UnityEngine.UI;

namespace Gallop
{
    public interface IParentColorReciever
    {
        GameObject gameObject {get;}
        bool IsIgnoreParentColor {get;set;}
        Color recieveColor {get;set;}
        Color color {get;set;}
        void UpdateColor();
    }

    public class ColorSender : MonoBehaviour
    {
        [SerializeField] protected Color _sendColor=Color.white;
        protected readonly List<IParentColorReciever> _recieverList=new List<IParentColorReciever>(4);
        UiGraphicColorSender _parentSender;
        readonly LiveColorSenderControl state=new LiveColorSenderControl();
        public Color SendColor {get=>_sendColor;set=>_sendColor=value;}
        public bool IsDirty=>state.IsDirty;
        public bool IsDirtyHierarchy=>state.IsDirtyHierarchy;
        public bool IsLateUpdated {get=>state.IsLateUpdated;protected set=>state.IsLateUpdated=value;}
        void Bind()
        {
            state.GetParent=()=>_parentSender!=null?_parentSender.Control:null;
            state.ParentActive=()=>_parentSender.isActiveAndEnabled;
            state.RebuildChildren=SetupChildReciever;
            state.UpdateSendColor=()=>{UpdateSendColor();state.SendColor=_sendColor;};
            state.SendToChildren=SendColorToChild;
        }
        internal LiveColorSenderControl Control {get {Bind();state.SendColor=_sendColor;return state;}}
        protected virtual void Awake(){Bind();SetupChildReciever();UpdateParentSender();}
        void OnEnable(){state.OnEnable();}
        public virtual void OnTransformParentChanged(){UpdateParentSender();state.IsDirty=true;}
        void OnTransformChildrenChanged(){SetDirtyHierarchy();}
        protected virtual void Update(){ResetLateUpdateFlag();}
        public void LateUpdate(){Bind();state.SendColor=_sendColor;state.LateUpdate();}
        public void ResetLateUpdateFlag(){state.ResetLateUpdateFlag();}
        public void SetDirty(){state.IsDirty=true;}
        public void SetDirtyHierarchy(){state.IsDirtyHierarchy=true;}
        public void SetCommonInteractableColor(bool isInteractable)
        {
            _sendColor=(ImageCommon.OriginalButtonColor??throw new System.InvalidOperationException("Original ButtonCommonStatic colours are not installed."))(isInteractable);
        }
        protected virtual void UpdateSendColor(){}
        protected virtual void SendColorToChild(Color color)
        {
            foreach(var receiver in _recieverList)
            {
                if(receiver.IsIgnoreParentColor)continue;
                receiver.recieveColor=color;
                receiver.UpdateColor();
            }
        }
        void UpdateParentSender()
        {
            var p=transform.parent;
            _parentSender=p!=null?p.GetComponentInParent<UiGraphicColorSender>():null;
        }
        void SetupChildReciever()
        {
            _recieverList.Clear();
            GetComponentsInChildren(true,_recieverList);
            for(int i=_recieverList.Count-1;i>=0;i--)
            {
                var receiver=_recieverList[i];
                if(receiver.gameObject==gameObject){_recieverList.RemoveAt(i);continue;}
                var p=receiver.gameObject.transform.parent;
                if(p==null)continue;
                var nearest=p.GetComponentInParent<ColorSender>();
                if(nearest!=null&&nearest==this)continue;
                // Native checks exact class identity, not an inheritance test.
                if(nearest!=null&&nearest.GetType().FullName=="Gallop.ButtonColorSender")
                {
                    var button=(UiGraphicColorSender)nearest;
                    if(button.TargetGraphic!=null&&button.TargetGraphic.gameObject==receiver.gameObject)continue;
                }
                _recieverList.RemoveAt(i);
            }
            state.IsDirtyHierarchy=false;
        }
        public static void SetDirtyParentSender(Transform target)
        {
            if(target==null)throw new System.NullReferenceException();
            while(true)
            {
                var sender=target.GetComponentInParent<UiGraphicColorSender>();
                if(sender==null)return;
                if(sender.isActiveAndEnabled){sender.SetDirty();return;}
                target=sender.transform.parent;
                if(target==null)return;
            }
        }
        public static void SetDirtyHierarchyParentSender(Transform target)
        {
            if(target==null)throw new System.NullReferenceException();
            var senders=target.GetComponentsInParent<UiGraphicColorSender>();
            if(senders==null)return;
            foreach(var sender in senders)if(sender.isActiveAndEnabled)sender.SetDirtyHierarchy();
        }
    }

    public class UiGraphicColorSender : ColorSender
    {
        public Graphic TargetGraphic {get;protected set;}
        protected override void Awake(){UpdateTargetGraphic();base.Awake();}
        public override void OnTransformParentChanged(){UpdateTargetGraphic();base.OnTransformParentChanged();}
        protected virtual void UpdateTargetGraphic()
        {
            var target=transform.GetComponent<Graphic>();
            if(target==null)target=transform.GetComponentInParent<Graphic>();
            if(TargetGraphic!=target){TargetGraphic=target;SetDirty();}
        }
        protected override void UpdateSendColor()
        {
            _sendColor=TargetGraphic==null?Color.white:
                LiveColorSenderControl.Multiply(TargetGraphic.color,TargetGraphic.canvasRenderer.GetColor());
        }
    }
}
