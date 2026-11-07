using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class StageController
    {
        // Native UpdateObject 0x500add0 / UpdateUseScreenCapture 0x500c8d0.
        // Commit the cached selection only after the image exists; while a
        // capture is being registered, retry instead of latching a white sheet.
        void UpdateObjectImage(StageObjectWorkInfo work,ref ObjectUpdateInfo info)
        {
            if(work.renderers.Length==0||work.renderers[0]==null)return;
            var owner=Director.instance;
            if(work.UseScreenCapture!=info.UseScreenCapture||work.ScreenCaptureId!=info.ScreenCaptureId||
                work.UseCaptureAspectType!=info.UseCaptureAspectType)
            {
                Texture image=info.UseScreenCapture
                    ? owner?.ScreenCaptures?.GetImageWithAspectCrop(info.UseCaptureAspectType,info.ScreenCaptureId)
                    : work.OriginMainTexture;
                if(!info.UseScreenCapture||image!=null)
                {
                    var material=work.renderers[0].material;
                    var previous=material.mainTexture;material.mainTexture=image;
                    if(previous!=null&&previous!=work.OriginMainTexture&&
                        owner?.ScreenCaptures?.IsRegisterTexture(previous)!=true&&
                        owner?.BackgroundImages?.Owns(previous)!=true)Destroy(previous);
                    work.UseScreenCapture=info.UseScreenCapture;work.ScreenCaptureId=info.ScreenCaptureId;
                    work.UseCaptureAspectType=info.UseCaptureAspectType;
                }
            }
            if(work.Use2dBackground!=info.Use2dBackground||work.BgId!=info.BgId||work.BgSubId!=info.BgSubId)
            {
                Texture image=info.Use2dBackground?owner?.BackgroundImages?.Get(info.BgId,info.BgSubId):work.OriginMainTexture;
                if(!info.Use2dBackground||image!=null)
                {
                    work.renderers[0].material.mainTexture=image;
                    work.Use2dBackground=info.Use2dBackground;work.BgId=info.BgId;work.BgSubId=info.BgSubId;
                }
            }
        }
    }
}
