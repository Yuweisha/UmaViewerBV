using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Gallop.Live;
using UnityEngine;

public partial class UmaContainerCharacter
{
    public LiveDirtMaterialBindings LiveDirtBindings { get; private set; }
    public LiveDirtTextureContext LiveDirtContext { get; private set; }
    public readonly List<string> LiveDirtResourceRequests=new List<string>();
    public LiveCharacterPlaybackOptions LivePlaybackOptions { get; private set; }

    public void ApplyLivePlaybackOptions(Gallop.Live.Cutt.LiveTimelineCharacterSettings settings,int slot)
    {
        if(LivePlaybackOptions==null)LivePlaybackOptions=new LiveCharacterPlaybackOptions();
        LivePlaybackOptions.Apply(settings,slot);
        // Original LoadCharaModel gates the call before the loader checks the
        // dress capability. Asset availability never enables this song option.
        if(LivePlaybackOptions.IsDirt)LoadLiveDirtTextures();
    }

    public void LoadLiveDirtTextures()
    {
        var dress=UmaDatabaseController.Instance.ReadDressData(LiveMasterDressId);
        if(dress==null){LiveDirtContext=new LiveDirtTextureContext{HasDress=false};return;}
        int D(string name)=>Convert.ToInt32(dress[name]);
        bool mob=IsMob;
        LiveDirtContext=new LiveDirtTextureContext
        {
            CharacterId=mob?1:CharaEntry.Id,MobId=mob?CharaEntry.Id:0,HeadSubId=LiveHeadModelSubId,DressId=LiveMasterDressId,
            BodyType=D("body_type"),BodySub=D("body_type_sub"),UseGender=D("use_gender"),BodyShape=D("body_shape"),IsDirt=D("is_dirt"),
            GeneralDress=D("chara_id")==0,
            Gender=Convert.ToInt32(CharaData["sex"]),PersonalDress=!mob&&Convert.ToInt32(CharaData["personal_dress"])!=0,
            AttachmentId=Convert.ToInt32(CharaData["attachment_model_id"]),MobFace=mob?Convert.ToInt32(CharaData["chara_face_model"]):0,
            // Original CharacterBuildPathInfo constructor initializes this to String.Empty.
            AttachmentSwapTextureName=""
        };
        LiveDirtTextureArray=LiveDirtTextureLoader.Load(LiveDirtContext,LoadLiveDirtResource,LiveDirtTextureArray);
    }

    Texture LoadLiveDirtResource(string path)
    {
        string key=path.ToLowerInvariant();LiveDirtResourceRequests.Add(path);
        if(!UmaViewerMain.Instance.AbList.TryGetValue(key,out var entry))return null;
        foreach(var dependency in UmaAssetManager.SearchAB(UmaViewerMain.Instance,entry))
            if(!LoadedAssets.Contains(dependency))LoadedAssets.Add(dependency);
        string textureName=key.Substring(key.LastIndexOf('/')+1);
        return entry.GetAll<Texture2D>().SingleOrDefault(t=>string.Equals(t.name,textureName,StringComparison.OrdinalIgnoreCase));
    }

    internal void InstallLiveDirtMaterials(LiveRendererHolder holder)
    {
        if(LiveDirtContext==null||!LiveDirtContext.HasDress)return;
        LiveDirtBindings=LiveDirtMaterialBindings.Register(holder.RendererArray,holder.MaterialArray,
            LiveDirtContext.CharacterId,LiveDirtContext.HeadSubId,LiveDirtContext.BodySub,
            LiveDirtContext.BodyType,LiveDirtContext.BodySub,LiveDirtContext.GeneralDress);
        LiveDirtBindings.Install(LiveDirtTextureArray);
    }
}
