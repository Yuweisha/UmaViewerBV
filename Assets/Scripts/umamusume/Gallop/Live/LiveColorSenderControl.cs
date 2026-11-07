using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gallop.Live
{
    // The original parent scheduler. Hierarchy/Graphic calls remain engine services;
    // unlike an independent tint animation it preserves the parent's frame flags.
    public sealed class LiveColorSenderControl
    {
        public Color SendColor=Color.white, OldColor=Color.white;
        public bool IsDirty, IsDirtyHierarchy, IsLateUpdated;
        public Func<LiveColorSenderControl> GetParent;
        public Func<bool> ParentActive;
        public Action RebuildChildren, UpdateSendColor;
        public Action<Color> SendToChildren;
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F(float v)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
        public static Color Multiply(Color a,Color b)=>new Color(F(a.r*b.r),F(a.g*b.g),F(a.b*b.b),F(a.a*b.a));
        public void OnEnable(){OldColor=Color.white;IsDirtyHierarchy=true;}
        public void ResetLateUpdateFlag(){IsLateUpdated=false;}
        public void LateUpdate()
        {
            if(IsLateUpdated)return;
            if(IsDirtyHierarchy){RebuildChildren();IsDirty=true;}
            var p=GetParent();
            if(p!=null&&p.IsDirty&&!p.IsLateUpdated&&ParentActive())p.LateUpdate();
            UpdateSendColor();
            float r=F(SendColor.r-OldColor.r),g=F(SendColor.g-OldColor.g),
                b=F(SendColor.b-OldColor.b),a=F(SendColor.a-OldColor.a);
            float distance=F(F(F(F(g*g)+F(r*r))+F(b*b))+F(a*a));
            // Original Color operator == compares squared distance against 1e-10.
            // Unordered (NaN) is unequal, so it also schedules propagation.
            if(!(distance<F(0.00001f*0.00001f)))IsDirty=true;
            OldColor=SendColor;
            if(IsDirty){SendToChildren(SendColor);IsDirty=false;}
            IsLateUpdated=true;
        }
    }
}
