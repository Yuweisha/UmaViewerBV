using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // URP14 supplies the physical surfaces owned by its renderer. This adapter
    // borrows them for a pass; it allocates/destroys no textures and never treats
    // a shader global binding as a temporary render target allocation.
    public sealed class NativeCameraSurfaceScope:IDisposable
    {
        readonly CameraData owner;
        readonly RenderTexture color,depth;
        readonly bool msaa;
        public static Action<CameraData,bool,bool,ScriptableRenderContext> Observer {get;set;}
        public NativeCameraSurfaceScope(CameraData camera,ref RenderingData data,ScriptableRenderContext context)
        {
            owner=camera;if(owner==null)throw new NullReferenceException();color=owner.ColorTextureMSAA;depth=owner.DepthTexture;msaa=owner.IsMSAA;
            bool borrowedDepth=false,borrowedColor=false;
            if(depth==null)
            {
                var handle=data.cameraData.renderer.cameraDepthTargetHandle;
                var texture=data.cameraData.renderer is GallopRenderer gallop ? gallop.DepthSurface : handle?.rt;
                if(texture!=null){owner.DepthTexture=texture;borrowedDepth=true;}
            }
            if(color==null&&depth==null)
            {
                var handle=data.cameraData.renderer.cameraColorTargetHandle;
                var texture=data.cameraData.renderer is GallopRenderer gallop ? gallop.MultisampleSurface : handle?.rt;
                if(texture!=null&&texture.antiAliasing>1){owner.ColorTextureMSAA=texture;owner.IsMSAA=true;borrowedColor=true;}
            }
            Observer?.Invoke(owner,borrowedDepth,borrowedColor,context);
        }
        public void Dispose(){owner.ColorTextureMSAA=color;owner.DepthTexture=depth;owner.IsMSAA=msaa;}
    }
    public static class NativeCameraAntiAlias
    {
        public struct Result {public int Level;public bool CreateTexture;}
        // Original CameraData.UpdateAntiAliasParameter, with the actual settings
        // singleton and camera engine getters supplied by their owners.
        public static Result Evaluate(int request,bool hasSettings,bool useOverride,int overrideLevel,bool allowMSAA,bool hasTarget,int settingsLevel)
        {
            int level=request;
            if(hasSettings&&useOverride)level=overrideLevel;
            if(level!=0){if(!allowMSAA)level=1;}
            else level=settingsLevel;
            return new Result{Level=level,CreateTexture=level>1&&hasTarget};
        }
    }
}
