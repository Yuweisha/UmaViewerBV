using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // Original SetEar 4ac9d30, EarWeightGroup Previous 4acffc0 and the ear
    // branch of global BlendProgrammable 4ac28a0. This is the programmable
    // nonrandom path; animation locator sampling is a separate producer.
    public sealed class LiveEarWeightTransition
    {
        public readonly float[] Weights = new float[306]; // current/target/previous, 17*6 each
        public readonly float[] Elapsed = new float[6], Duration = new float[6];
        public readonly List<int> ActiveTypes = new List<int> { 0 };
        readonly List<int>[] sideTypes = { new List<int>(), new List<int>() };
        bool dirty;

        public LiveEarWeightTransition()
        { for (int i = 0; i < 6; i++) { Weights[i] = 1f; Weights[204 + i] = 1f; } }

        void AddType(int type) { if (!ActiveTypes.Contains(type)) ActiveTypes.Add(type); }

        public void SetAnimation(DrivenKeyAnimation animation,float duration=0f,bool force=false)
        {
            if(duration>0f)
            {
                for(int side=0;side<2;side++)
                    if(force||Elapsed[side]==0f||Elapsed[side]>=Duration[side])
                        for(int ear=0;ear<17;ear++)for(int bone=0;bone<3;bone++)Weights[204+ear*6+side*3+bone]=Weights[ear*6+side*3+bone];
                for(int i=0;i<6;i++){Duration[i]=duration;Elapsed[i]=0f;}
                Array.Clear(Weights,102,102);
            }
            for (int side = 0; side < 2; side++)
            {
                sideTypes[side].Clear();
                if(duration>0f)foreach(int ear in ActiveTypes)if(ear!=0)sideTypes[side].Add(ear);
                if (side >= animation.animationEarType.Length) continue;
                var types = animation.animationEarType[side];
                // Global programmable blend uses count; animation locator
                // sampling below instead uses the full saved array length.
                for (int i = 0; i < types.count; i++)if(!sideTypes[side].Contains((int)types.faceType[i]))sideTypes[side].Add((int)types.faceType[i]);
            }
            dirty = false;
        }

        public void ApplyAnimation(DrivenKeyAnimation animation, Transform[,] locators)
        {
            ActiveTypes.Clear(); ActiveTypes.Add(0);
            for (int side = 0; side < animation.animationEarType.Length; side++)
                foreach (var ear in animation.animationEarType[side].faceType)
                {
                    var node = locators[side, (int)ear]; if (node == null) continue;
                    var position = node.localPosition; int offset = side * 3;
                    int bank = Duration[offset] > Elapsed[offset] ? 102 : 0;
                    int index = bank + (int)ear * 6 + offset;
                    float x = (float)((double)position.x * -100.0);
                    float y = (float)((double)position.y * 100.0);
                    float z = (float)((double)position.z * 100.0);
                    Weights[index] = x; Weights[index + 1] = y; Weights[index + 2] = z;
                    float sum = (float)((double)x + y); sum = (float)((double)sum + z);
                    if (sum > Mathf.Epsilon) AddType((int)ear);
                }
        }

        public void Set(int type, int side, float weight, float duration, bool force = true)
        {
            if ((uint)type > 16 || (uint)side > 1) throw new ArgumentOutOfRangeException();
            int offset = side * 3, bank = 0;
            if (duration > 0f)
            {
                // The original virtual override reads time[side], while the
                // blend clock is time[side*3]. Keep that literal ABI behavior.
                if (force || Elapsed[side] == 0f || Elapsed[side] >= Duration[side])
                    for (int ear = 0; ear < 17; ear++) for (int bone = 0; bone < 3; bone++)
                        Weights[204 + ear * 6 + offset + bone] = Weights[ear * 6 + offset + bone];
                Duration[offset] = duration; Elapsed[offset] = 0f; bank = 102;
            }
            if (dirty)
            {
                int[] previous = ActiveTypes.ToArray();
                // Original ClearEarBlendFaces uses sideTypes[0] as scratch
                // for GetEarTypes(removeBase=false). A one-sided left Set
                // leaves that right-side list containing base until a right
                // submission replaces it. Normal parent submits both sides.
                sideTypes[0].Clear(); sideTypes[0].AddRange(previous);
                ActiveTypes.Clear(); ActiveTypes.Add(0);
                foreach (int ear in previous)
                    if (type == 0 || Weights[ear * 6] > 0f || Weights[ear * 6 + 3] > 0f) AddType(ear);
            }
            dirty = false;
            for (int ear = 1; ear < 17; ear++) for (int bone = 0; bone < 3; bone++)
                Weights[bank + ear * 6 + offset + bone] = 0f;
            for (int bone = 0; bone < 3; bone++) Weights[bank + type * 6 + offset + bone] = weight;
            if (weight > 0f) AddType(type);
            sideTypes[side].Clear();
            foreach (int ear in ActiveTypes) if (ear != 0) sideTypes[side].Add(ear);
            // duration<=0 intentionally leaves the previous clock/banks intact.
        }

        public void Advance(float delta, DrivenKeyComponent.InterpolateType interpolate)
        {
            for (int side = 0; side < 2; side++)
            {
                int offset = side * 3;
                if (Elapsed[offset] >= Duration[offset]) continue;
                Elapsed[offset] = (float)((double)Elapsed[offset] + delta);
                float rate = LiveFacialInterpolation.Rate(interpolate, Elapsed[offset], Duration[offset]);
                foreach (int ear in sideTypes[side]) for (int bone = 0; bone < 3; bone++)
                {
                    int index = ear * 6 + offset + bone; float previous = Weights[204 + index], target = Weights[102 + index];
                    if (previous == target) continue;
                    float difference = (float)((double)target - previous);
                    float step = (float)((double)difference * rate);
                    Weights[index] = (float)((double)previous + step);
                }
            }
            dirty = true;
        }
    }
}
