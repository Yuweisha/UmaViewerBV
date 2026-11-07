using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gallop.Live
{
    // CheekController's non-Race Mini owner path. Explicit initialized
    // RendererHolder cache/object/texture inputs preserve the engine boundary.
    public sealed class LiveMiniCheekRuntime
    {
        readonly Transform _locatorRoot;
        readonly bool _drivenKeyControl;
        readonly int _controlType;
        GameObject _object;
        Material[] _rendererMaterialArray;
        Material _material;
        Texture _texture0, _texture1, _whiteTexture;
        bool _isInitialized, _isDrivenKeyControll, _isCheekVisible, _isVisibleFromDrivenKey;
        int _currentCheekIndex;
        float _cheekThresholdValue, _cheekThresholdAddend, _timeCount, _timeMax;
        Transform _cheekLocator;
        public bool IsInitialized => _isInitialized;
        public bool HasRendererMaterialArray => _rendererMaterialArray != null;
        public bool IsDrivenKeyControll { get => _isDrivenKeyControll; set => _isDrivenKeyControll=value; }
        public Func<bool,float,float,float> CalcThresholdValue;
        public event Action<float> MulCheekRate;
#if UNITY_EDITOR
        public event Action<string,Material,Texture,float> EngineWrite;
        public event Action<bool,int> OwnerVisibleWrite;
#endif
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F(float value)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
        public LiveMiniCheekRuntime(Transform locatorRoot,bool drivenKeyControl=true,int controlType=1)
        { _locatorRoot=locatorRoot;_drivenKeyControl=drivenKeyControl;_controlType=controlType; }

        public void Initialize(LiveRendererHolder holder,GameObject cheekObject,Texture texture0,Texture texture1)
        {
            if(holder==null)return;
            _object=null;_material=null;_texture0=null;_texture1=null;_whiteTexture=Texture2D.whiteTexture;
            _isInitialized=false;_rendererMaterialArray=null;_object=cheekObject;
            if(_object==null)return;
            _cheekLocator=_locatorRoot.Find("Cheek_Ctrl");
            int index=holder.FindIndex(cheekObject);
            if(index<0)return; // Original RendererHolder.FindIndex miss.
            _rendererMaterialArray=holder.GetMaterialArray(index);
            if(_rendererMaterialArray==null)return;
            if(_rendererMaterialArray.Length>0)_material=_rendererMaterialArray[0];
            _texture0=texture0;_texture1=texture1;
            if(_material==null||_texture0==null||_texture1==null)return;
            _isInitialized=true;_isDrivenKeyControll=_drivenKeyControl;_currentCheekIndex=-1;
            SetVisible(false);
        }
        void SetTexture(string property,Texture texture)
        {
            if(property=="_MainTex")_material.mainTexture=texture;else _material.SetTexture(property,texture);
#if UNITY_EDITOR
            EngineWrite?.Invoke(property,_material,texture,0);
#endif
        }
        void SetActive(bool value)
        {
            _object.SetActive(value);
#if UNITY_EDITOR
            EngineWrite?.Invoke("GameObject.SetActive",null,null,value?1:0);
#endif
        }
        public int RenderQueue
        {
            get=>_material!=null?_material.renderQueue:-1;
            set{if(_material==null)return;_material.renderQueue=value;
#if UNITY_EDITOR
                EngineWrite?.Invoke("Material.renderQueue",_material,null,value);
#endif
            }
        }
        public void SetCheekRate(float value)
        {
            if(!_isInitialized)return;
            float clamped=value<0?0:value>1?1:value;
            foreach(var material in _rendererMaterialArray)
            {
                material.SetFloat("_CheekRate",clamped);
#if UNITY_EDITOR
                EngineWrite?.Invoke("_CheekRate",material,null,clamped);
#endif
            }
            MulCheekRate?.Invoke(value);
        }
        public void SetVisible(bool visible,int index=0,float time=0,bool previousVisible=false,int previousIndex=0)
        {
            if(!_isInitialized||(_isCheekVisible==visible&&_currentCheekIndex==index))return;
            _cheekThresholdValue=0;
            SetTexture("_PreviousCheekTex",previousVisible?(previousIndex>0?_texture1:_texture0):_whiteTexture);
            SetTexture("_MainTex",visible?(index>0?_texture1:_texture0):_whiteTexture);
            SetActive(true);
            float difference=F(1f-_cheekThresholdValue);_timeMax=F(difference*time);
            if(!(_timeMax>0)){_cheekThresholdValue=1;SetCheekRate(1);SetActive(visible);}
            else{_cheekThresholdAddend=F(difference/time);SetCheekRate(_cheekThresholdValue);}
            _isCheekVisible=visible;_currentCheekIndex=index;
        }
        void OwnerSetVisible(bool value,int index=0)
        {
#if UNITY_EDITOR
            OwnerVisibleWrite?.Invoke(value,index);
#endif
            SetVisible(value,index);
        }
        public void UpdateFromLocator()
        {
            if(!_isDrivenKeyControll||_cheekLocator==null)return;
            float first=0,second=0;
            if(_controlType==0){first=Mathf.Max(0,F(_cheekLocator.localScale.x-0.0001f));second=Mathf.Max(0,F(_cheekLocator.localScale.y-0.0001f));}
            else if(_controlType==1){first=F(_cheekLocator.localPosition.x*100f);second=F(_cheekLocator.localPosition.z*100f);}
            if(first>0){OwnerSetVisible(true);SetCheekRate(first);_isVisibleFromDrivenKey=true;}
            else if(second>0){OwnerSetVisible(true,1);SetCheekRate(second);_isVisibleFromDrivenKey=true;}
            else if(_isVisibleFromDrivenKey){OwnerSetVisible(false);_isVisibleFromDrivenKey=false;}
        }
        public void OnUpdate(float deltaTime)
        {
            if(!_isInitialized)return;
            UpdateFromLocator();
            if(!(_timeMax>0))return;
            _timeCount=F(_timeCount+deltaTime);
            if(!(_timeCount>=_timeMax))
            {
                _cheekThresholdValue=CalcThresholdValue!=null?CalcThresholdValue(_isCheekVisible,_timeCount,_timeMax):F(F(deltaTime*_cheekThresholdAddend)+_cheekThresholdValue);
                SetCheekRate(_cheekThresholdValue);
            }
            else
            {
                if(!_isCheekVisible){_cheekThresholdValue=0;SetActive(false);}
                else{_cheekThresholdValue=1;SetCheekRate(1);}
                _timeCount=0;_timeMax=0;
            }
        }
    }
}
