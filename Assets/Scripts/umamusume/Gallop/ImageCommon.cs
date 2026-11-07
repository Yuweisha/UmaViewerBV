using System;
using Gallop.Live;
using UnityEngine;
using UnityEngine.UI;

namespace Gallop
{
    // Original component identity; original asset loader and button static table
    // are installed by their owners, not replaced with arbitrary editor resources.
    public class ImageCommon : Image,IParentColorReciever
    {
        [SerializeField] Color _defaultColor=Color.white;
        bool _isSetupedDefaultColor;
        [SerializeField] Color _mulColor=Color.white;
        [SerializeField] bool _isIgnoreParentColor;
        Color _receiveColor=Color.white;
        [SerializeField] string _atlasName="",_spriteName="",_materialPath="";
        public static Func<string,Material> OriginalMaterialLoader {get;set;}
        public static Func<bool,Color> OriginalButtonColor {get;set;}
        LiveImageCommonControl<UnityEngine.Object> control;
        LiveImageCommonControl<UnityEngine.Object> GetControl()
        {
            if(control==null)control=new LiveImageCommonControl<UnityEngine.Object>(new UnityLiveImageCommonServices(this,()=>m_Material,
                AtlasUtil.GetSpriteByName,
                path=>(OriginalMaterialLoader??throw new InvalidOperationException("Original ResourceManager material loader is not installed."))(path),
                value=>(OriginalButtonColor??throw new InvalidOperationException("Original ButtonCommonStatic colours are not installed."))(value),
                ColorSender.SetDirtyParentSender,ColorSender.SetDirtyHierarchyParentSender,
                BaseAwake,BaseParentChanged,BaseDestroy));
            control.DefaultColor=_defaultColor;control.SetupDefault=_isSetupedDefaultColor;
            control.MulColor=_mulColor;control.IgnoreParentColor=_isIgnoreParentColor;control.ReceiveColor=_receiveColor;
            control.AtlasName=_atlasName;control.SpriteName=_spriteName;control.MaterialPath=_materialPath;
            return control;
        }
        void Invoke(Action<LiveImageCommonControl<UnityEngine.Object>> action)
        {
            var c=GetControl();
            try {action(c);}
            finally {_defaultColor=c.DefaultColor;_isSetupedDefaultColor=c.SetupDefault;_mulColor=c.MulColor;_isIgnoreParentColor=c.IgnoreParentColor;_receiveColor=c.ReceiveColor;}
        }
        void BaseAwake(){base.Awake();}void BaseParentChanged(){base.OnTransformParentChanged();}void BaseDestroy(){base.OnDestroy();}
        public Color DefaultColor=>_defaultColor;
        public bool IsIgnoreParentColor {get=>_isIgnoreParentColor;set=>Invoke(c=>c.SetIgnoreParentColor(value));}
        public Color recieveColor {get=>_receiveColor;set=>_receiveColor=value;}
        protected override void Awake(){Invoke(c=>c.Awake());}
        protected override void OnTransformParentChanged(){Invoke(c=>c.OnTransformParentChanged());}
        protected override void OnDestroy(){Invoke(c=>c.OnDestroy());}
        public void SetDefaultColor(Color value){_defaultColor=value;}
        public void SetMulColor(Color value){Invoke(c=>c.SetMulColor(value));}
        public Color GetMulColor()=>_mulColor;
        public void SetAlpha(float value){Invoke(c=>c.SetAlpha(value));}
        public void SetInteractableMulColor(bool value){Invoke(c=>c.SetInteractableMulColor(value));}
        public void UpdateColor(){Invoke(c=>c.UpdateColor());}
        public void ReconnectReference(){Invoke(c=>c.ReconnectReference());}
    }
}
