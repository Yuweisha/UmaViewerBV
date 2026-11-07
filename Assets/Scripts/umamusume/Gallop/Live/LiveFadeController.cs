using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gallop.Live
{
    public interface ILiveFadeSurface
    {
        bool IsAlive {get;}
        Color Color {get;set;}
    }
    // RawImageCommon's virtual color access is an explicit UI boundary.
    public sealed class LiveFadeController
    {
        private ILiveFadeSurface _landscapeImage,_portraitImage,_fullPortraitImage;
        private float _startCurrentTime=-1f,_duration=-1f;
        private Color _color,_startColor=Color.clear,_targetColor;
        private bool _isInitialized;
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F(float v)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
        public void Initialize(ILiveFadeSurface landscape,ILiveFadeSurface portrait,ILiveFadeSurface fullPortrait)
        {
            if(landscape==null||!landscape.IsAlive||portrait==null||!portrait.IsAlive||fullPortrait==null||!fullPortrait.IsAlive)return;
            _landscapeImage=landscape;_portraitImage=portrait;_fullPortraitImage=fullPortrait;
            _startCurrentTime=-1f;_duration=-1f;_startColor=Color.clear;
            Publish(_startColor);_isInitialized=true;
        }
        void Publish(Color color)
        {
            _landscapeImage.Color=color;
            _portraitImage.Color=color;
            _fullPortraitImage.Color=color;
        }
        public void SetColor(Color color)
        {
            if(!_isInitialized)return;
            Publish(color);_startCurrentTime=-1f;_duration=-1f;_startColor=color;_targetColor=color;
        }
        public void Start(Color color,float currentTime,float duration)
        {
            if(!_isInitialized)return;
            if(duration>0f)
            {
                Color start=_landscapeImage.Color;
                _startCurrentTime=currentTime;_duration=duration;_startColor=start;_targetColor=color;
            }
            else SetColor(color);
        }
        public void AlterUpdate(float currentTime)
        {
            if(!_isInitialized)return;
            // COMISS/JB and JBE route unordered start/time into the active path.
            if(float.IsNaN(_startCurrentTime)||float.IsNaN(currentTime)||_startCurrentTime<0f||_startCurrentTime<=currentTime)
            {
                if(!(_duration>0f))return;
                float ratio=F(F(currentTime-_startCurrentTime)/_duration);
                if(ratio<1f)
                {
                    _color=new Color(F(F(F(_targetColor.r-_startColor.r)*ratio)+_startColor.r),
                        F(F(F(_targetColor.g-_startColor.g)*ratio)+_startColor.g),
                        F(F(F(_targetColor.b-_startColor.b)*ratio)+_startColor.b),
                        F(F(F(_targetColor.a-_startColor.a)*ratio)+_startColor.a));
                    Publish(_color);
                }
                else Publish(_targetColor);
            }
            else if(currentTime>0f)
            {
                Publish(_startColor);_startCurrentTime=-1f;_duration=-1f;
            }
        }
    }
}
