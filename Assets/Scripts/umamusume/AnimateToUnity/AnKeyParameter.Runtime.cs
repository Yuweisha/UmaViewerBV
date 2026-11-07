
using UnityEngine;

namespace AnimateToUnity
{
    public partial class AnKeyParameter
    {
        public void Initialize()=>_keyCount=_keyList.Count;
        static float F(float value)=>AnFloat.Round(value);
        public float GetValue(float original,float currentTime,float previousTime,float objectTime,ref int startIndex,bool stabilizeRotation=false)
        {
            if(_keyCount==0)return original;
            if(_keyList[0].x>=objectTime){startIndex=0;return _keyList[0].y;}
            if(objectTime>=_keyList[_keyCount-1].x){startIndex=_keyCount-1;return _keyList[_keyCount-1].y;}
            if(previousTime>currentTime||_keyList[startIndex].x>objectTime||startIndex>=_keyCount||startIndex<0)startIndex=0;
            int previous=0;
            for(int index=startIndex;index<_keyCount;index++)
            {if(_keyList[index].x>objectTime)break;previous=index;}
            startIndex=previous;Vector2 a=_keyList[previous],b=_keyList[previous+1];float duration=F(b.x-a.x);
            if(.001f>duration)return b.y;
            float ratio=F(F(objectTime-a.x)/duration);
            if(stabilizeRotation&&Mathf.Abs(F(a.y-b.y))>180f)
                return Quaternion.Slerp(Quaternion.Euler(0f,0f,a.y),Quaternion.Euler(0f,0f,b.y),ratio).eulerAngles.z;
            return F(F(F(b.y-a.y)*ratio)+a.y);
        }
    }
}
