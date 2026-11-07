using System;
using UnityEngine;

namespace Gallop.RenderPipeline
{
    // Installed-game SHA e16c1636...3587, metadata31, 2026-10-12.
    // LensFlareManager.MakeGeometry RVA 0x51CE690; UV helper 0x51CDB20.
    // Pure draw preparation: visibility must come from the camera's raycast/fade
    // history. Never interpret source Color.a as RGB opacity (native RGB One/One).
    public static class NativeLensFlareGeometry
    {
        public struct ElementDraw
        {
            public Vector2 Size;
            public Vector4 UV;
            public Color Color;
            public Vector3 CameraPosition;
        }

        public static void UVCoords(int layout, int image, Vector2 pixelOffset, out Vector2 uv0, out Vector2 uv1)
        {
            if (layout < 0 || layout > 5 || image < 0) throw new ArgumentOutOfRangeException();
            if (layout == 0)
            {
                if (image == 0) { uv0=pixelOffset; uv1=new Vector2(1,.5f)-pixelOffset; }
                else { int col=(image-1)&1, row=(image-1)>>1;
                    uv0=new Vector2(col*.5f,.5f+row*.25f)+pixelOffset;
                    uv1=new Vector2((col+1)*.5f,.5f+(row+1)*.25f)-pixelOffset; }
            }
            else if (layout == 1)
            {
                if (image == 0) { uv0=Vector2.zero; uv1=new Vector2(1,.5f)-pixelOffset; }
                else if (image == 1) { uv0=new Vector2(0,.5f); uv1=new Vector2(.5f,.75f)-pixelOffset; }
                else if (image == 2) { uv0=new Vector2(0,.75f); uv1=new Vector2(.5f,1)-pixelOffset; }
                else { int col=(image-3)&1, row=(image-3)>>1;
                    uv0=new Vector2(.5f+col*.25f,.5f+row*.125f)+pixelOffset;
                    uv1=new Vector2(.5f+(col+1)*.25f,.5f+(row+1)*.125f)-pixelOffset; }
            }
            else
            {
                int n=layout-1, col=image%n, row=image/n; float step=1f/n;
                uv0=new Vector2(col*step,row*step)+pixelOffset;
                uv1=new Vector2((col+1)*step,(row+1)*step)-pixelOffset;
            }
        }

        public static ElementDraw Prepare(LensFlareElement element, int layout, Vector2 pixelOffset,
            Vector3 cameraPosition, float visibility, Color tint, bool useFog, float fogDensity)
        {
            UVCoords(layout,element.ImageIndex,pixelOffset,out var uv0,out var uv1);
            uv0.y=1-uv0.y; uv1.y=1-uv1.y;
            float v=visibility;
            if(useFog) {
                // Native computes 1-(1-exp(-density*density*z*z)).
                float exponent=-(fogDensity*fogDensity*cameraPosition.z*cameraPosition.z);
                v*=1-(1-Mathf.Exp(exponent));
            }
            float factor=element.Zoom?v*.01f:.01f;
            float size=cameraPosition.z*element.Size*factor;
            Vector2 sizes=new Vector2(size,size);
            if(element.Rotate) {
                var radial=new Vector2(cameraPosition.x,cameraPosition.y);
                float length=radial.magnitude;
                radial=length>1e-5f?radial/length:Vector2.zero;
                sizes=radial*(size*1.4f);
            }
            float gamma=Mathf.LinearToGammaSpace(v);
            Color color=element.UseLightColor?element.Color*tint:element.Color;
            if(!element.UseLightColor || element.Fade) color*=gamma;
            color=new Color(Mathf.Clamp01(color.r),Mathf.Clamp01(color.g),Mathf.Clamp01(color.b),Mathf.Clamp01(color.a));
            var center=new Vector3(0,0,cameraPosition.z);
            return new ElementDraw{Size=sizes,UV=new Vector4(uv0.x,uv0.y,uv1.x,uv1.y),Color=color,
                CameraPosition=cameraPosition+(center-cameraPosition)*element.Position};
        }

        // UpdateJob.Execute 0x52167F0, EndUpdateFlare 0x51CDFA0.
        // Out-of-view and blocked sources decay by half of deltaTime*FadeSpeed;
        // unobstructed sources rise by the full amount. Brightness is separate.
        public static float AdvanceVisibility(float previous, bool active, bool inView, bool blocked, float fadeSpeed, float deltaTime)
        {
            if(!active) return 0;
            return !inView || blocked ? Mathf.Max(0,previous-deltaTime*fadeSpeed*.5f)
                : Mathf.Min(1,previous+deltaTime*fadeSpeed);
        }
    }
}
