using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gallop.Live.Cutt;
using Gallop.RenderPipeline;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.Live
{
    // Native 0x4fdf900 / CapturePipeline.SetupCapture 0x4d56540. Capture
    // post-processed camera color once, register next frame, retain by ID.
    public sealed class LiveScreenCaptureController
    {
        readonly Dictionary<int,Texture2D> images=new Dictionary<int,Texture2D>();
        readonly List<Texture2D> crops=new List<Texture2D>();
        readonly List<RenderTexture> pending=new List<RenderTexture>();
        readonly List<(Gallop.RenderPipeline.CameraData Owner,int Id)> passes=new List<(Gallop.RenderPipeline.CameraData,int)>();
        bool released;
        public IReadOnlyDictionary<int,Texture2D> Images=>images;

        public void ExecuteScreenCapture(MonoBehaviour host,Camera camera,int id,float scale)
        {
            if(released||camera==null||images.ContainsKey(id))return;
            var owner=camera.GetCameraData();
            ref var parameter=ref owner.GetCustomRenderParameter(out int reserve);
            passes.Add((owner,reserve));
            parameter.IsEnable=true;parameter.RenderPassEvent=(RenderPassEvent)600;
            parameter.OnExecute=(CustomRenderPass pass,ScriptableRenderContext context,ref RenderingData data)=>
            {
                var renderer=data.cameraData.renderer as GallopRenderer;
                var source=renderer!=null?renderer.OutputSurface:camera.targetTexture;
                int width=source!=null?source.width:data.cameraData.cameraTargetDescriptor.width;
                int height=source!=null?source.height:data.cameraData.cameraTargetDescriptor.height;
                var capture=new RenderTexture(Mathf.Max(1,(int)(width*scale)),Mathf.Max(1,(int)(height*scale)),0,
                    source!=null?source.format:RenderTextureFormat.ARGB32){name="LiveScreenCapture_"+id};
                capture.Create();pending.Add(capture);
                var cmd=CommandBufferPool.Get("Live ScreenCapture after postprocess");
                cmd.Blit(source!=null?new RenderTargetIdentifier(source):new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget),capture);
                context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
                host.StartCoroutine(RegisterImageNextFrame(id,capture));
                passes.Remove((owner,reserve));
                owner.ReleaseCustomRenderParameter(ref reserve);
            };
        }

        IEnumerator RegisterImageNextFrame(int id,RenderTexture capture)
        {
            yield return null;
            if(released)yield break;
            RegisterImage(id,capture);
            pending.Remove(capture);capture.Release();Object.Destroy(capture);
        }

        public void RegisterImage(int id,RenderTexture source)
        {
            if(images.ContainsKey(id))return;
            var previous=RenderTexture.active;
            var pixels=new Texture2D(source.width,source.height,TextureFormat.RGB24,false);
            try
            {
                RenderTexture.active=source;pixels.ReadPixels(new Rect(0,0,source.width,source.height),0,0);pixels.Apply();
                // Native TextureSaveLoaderUtil.CompressTexture uses JPEG90.
                var image=new Texture2D(2,2,TextureFormat.RGB24,false){name="LiveCaptureImage_"+id};
                image.LoadImage(pixels.EncodeToJPG(90));images.Add(id,image);
            }
            finally{RenderTexture.active=previous;Object.Destroy(pixels);}
        }

        public Texture2D GetImageWithAspectCrop(LiveTimelineKeyObjectData.ScreenCaptureAspectType aspect,int id)
        {
            if(!images.TryGetValue(id,out var image))return null;
            if(aspect!=LiveTimelineKeyObjectData.ScreenCaptureAspectType.LSize)return image;
            // Native0x4fdfae0: center crop to photographic L-size, not stretch.
            const float targetAspect=1.427f;
            int width=image.width,height=image.height;
            if((float)width/height>targetAspect)width=Mathf.RoundToInt(height*targetAspect);
            else height=Mathf.RoundToInt(width/targetAspect);
            var result=new Texture2D(width,height,image.format,false){name=image.name+"_LSize"};
            result.SetPixels(image.GetPixels((image.width-width)/2,(image.height-height)/2,width,height));result.Apply();
            crops.Add(result);return result;
        }

        public bool IsRegisterTexture(Texture image)=>images.Values.Any(value=>value==image);
        public void Release()
        {
            released=true;
            foreach(var pass in passes){if(pass.Owner==null)continue;int id=pass.Id;pass.Owner.ReleaseCustomRenderParameter(ref id);}passes.Clear();
            foreach(var image in images.Values)if(image!=null)Object.Destroy(image);images.Clear();
            foreach(var image in crops)if(image!=null)Object.Destroy(image);crops.Clear();
            foreach(var target in pending)if(target!=null){target.Release();Object.Destroy(target);}pending.Clear();
        }
    }
}
