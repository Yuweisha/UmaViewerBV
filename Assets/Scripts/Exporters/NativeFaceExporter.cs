using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Gallop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Authored driven-key data, without MMD conversion or guessed controls.</summary>
public static class NativeFaceExporter
{
    public static string PathOf(Transform t, Transform root)
    {
        if (!t) return null;
        if (t == root) return "";
        var names = new List<string>();
        for (; t && t != root; t = t.parent) names.Add(t.name);
        if (!t) throw new InvalidDataException("Transform is outside the character");
        names.Reverse(); return string.Join("/", names);
    }
    public static JArray V(Vector3 v) => new JArray(v.x,v.y,v.z);
    public static JArray Q(Quaternion q) => new JArray(q.x,q.y,q.z,q.w);
    public static string Hash(string path)
    {
        using (var stream=File.OpenRead(path)) using(var sha=SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    public static FacialMorph[] Controls(FaceDrivenKeyTarget f) =>
        f.EarMorphs.Concat(f.EyeBrowMorphs).Concat(f.EyeMorphs).Concat(f.MouthMorphs).ToArray();

    public static JObject Capture(UmaContainerCharacter c)
    {
        var f=c.FaceDrivenKeyTarget;
        if (!f || c.IsMini) throw new NotSupportedException("A full-size character with driven-key data is required");
        JObject overrideData=OverrideData(c.FaceOverrideData);
        using (var snapshot=new PMXExportStateSnapshot(c))
        {
            snapshot.FreezeForExport(); c.ResetBodyPose(); c.FaceOverrideData=null;
            f.ClearAllWeights(); f.ChangeMorph();
            var controls=Controls(f);
            Func<TrsArray,JObject> trs=a=>new JObject {
                ["sourcePath"]=a._path,["path"]=PathOf(a.transform,c.transform),
                ["position"]=V(a._position),["rotationMayaDegrees"]=V(a._rotation),["scale"]=V(a._scale),
                ["isValidScaleTransform"]=a._isValidScaleTransform,["isOverrideTarget"]=a.IsOverrideTarget };
            var nodes=new JArray(c.GetComponentsInChildren<Transform>(true).Select(t=>new JObject {
                ["path"]=PathOf(t,c.transform),["name"]=t.name,["parent"]=t==c.transform ? null:PathOf(t.parent,c.transform),
                ["position"]=V(t==c.transform?Vector3.zero:t.localPosition),
                ["rotation"]=Q(t==c.transform?Quaternion.identity:t.localRotation),
                ["scale"]=V(t==c.transform?Vector3.one:GlbCharacterExporter.NativeFaceReferenceScale(t.localScale)) }));
            var data=new JObject {
                ["schema"]="umaviewer.native-face/1",["character"]=c.CharaEntry.Id,["characterName"]=c.name,
                ["coordinates"]="Unity local TRS, meters; quaternion xyzw; authored Maya Euler degrees qZ*qY*qX",
                ["exchangeReference"]="nodes use non-singular reference scales; bases retain original tiny hiding scales; GLB has no baked facial shape keys",
                ["semanticsReference"]="Current UmaViewer C#; game DLL equivalence has not been established",
                ["nodes"]=nodes,
                ["bases"]=new JArray(f.BaseMorphs.Select(b=>new JObject { ["name"]=b.name,["trs"]=new JArray(b.trsArray.Select(trs)) })),
                ["controls"]=new JArray(controls.Select(m=>new JObject {
                    ["id"]=m.name,["index"]=m.index,["tag"]=m.tag,["left"]=m.direction,
                    ["group"]=f.EarMorphs.Contains(m)?"Ear":f.EyeBrowMorphs.Contains(m)?"Eyebrow":f.EyeMorphs.Contains(m)?"Eye":"Mouth",
                    ["locator"]=PathOf(m.locator,c.transform),["trs"]=new JArray(m.trsArray.Select(trs)) })),
                ["eyeRangeControls"]=new JArray(new[]{f.LeftEyeXrange,f.LeftEyeYrange,f.RightEyeXrange,f.RightEyeYrange}.Select(m=>m.name)),
                ["eyeLocators"]=new JArray(new[]{f.EyeballCtrl_L_Locator,f.EyeballCtrl_R_Locator,f.EyeballCtrl_All_Locator}.Select(t=>PathOf(t,c.transform))),
                ["headPath"]=PathOf(c.HeadBone.transform,c.transform),["eyeHeight"]=c.EyeHeight,
                ["override"]=overrideData,
                ["effects"]=new JArray(f.OtherMorphs.Select(m=>new JObject {
                    ["id"]=m.name,["locator"]=PathOf(m.locator,c.transform),
                    ["bindings"]=new JArray(m.BindProperties.Select(p=>new JObject {
                        ["type"]=p.Type.ToString(),["part"]=p.Part.ToString(),["default"]=p.DefaultValue,
                        ["property"]=p.PropertyName,["material"]=p.BindMaterial?p.BindMaterial.name:null,
                        ["texture"]=p.BindTexture?p.BindTexture.name:null,
                        ["prefabs"]=new JArray(p.BindPrefabs.Select(o=>PathOf(o.transform,c.transform))) })) })),
                ["emotions"]=new JArray((c.FaceEmotionKeyTarget?.FaceEmotionKey ?? new List<FaceTypeData>()).Select(e=>new JObject {
                    ["label"]=e.label,["source"]=new JObject { ["mouth"]=e.mouth,["eyeL"]=e.eye_l,["eyeR"]=e.eye_r,["eyebrowL"]=e.eyebrow_l,["eyebrowR"]=e.eyebrow_r },
                    ["keys"]=new JArray((e.emotionKeys ?? new List<EmotionKey>()).Select(k=>new JObject { ["id"]=k.morph?.name,["weightPercent"]=k.weight })) })),
                ["resources"]=new JArray(c.SourceAssets.Distinct().Select(e=>new JObject {
                    ["name"]=e.Name,["checksum"]=e.Checksum,["fileSha256"]=File.Exists(e.Path)?Hash(e.Path):null })),
                ["unsupported"]=new JArray("Effect bindings are preserved but not applied by the first Blender adapter",
                    "World-target eye tracking and Live timeline evaluation require separate parity tests") };
            return data;
        }
    }
    public static JObject OverrideData(FaceOverrideData d)
    {
        if (!d) return null;
        return new JObject { ["bothEyes"]=d.IsBothEyesSetting,["disableBlink"]=d.IsDisableEyeBlink,
            ["sets"]=new JArray((d.FaceOverrideArray ?? new FaceOverrideReplaceDataSet[0]).Select(s=>new JObject {
                ["onlyBase"]=s.IsOnlyBaseReplace,["baseIndex"]=(int)s.BaseReplaceFaceType,
                ["rules"]=new JArray((s.DataArray ?? new FaceOverrideReplaceData[0]).Select(a=>new JObject {
                    ["sources"]=new JArray(a.SrcArray.Select(i=>(int)i)),
                    ["destinations"]=new JArray(a.DstArray.Select(v=>new JObject { ["index"]=(int)v.Index,["weight"]=v.Weight })) })) })) };
    }
    public static void Export(UmaContainerCharacter c,string path)
    {
        string model=System.IO.Path.ChangeExtension(path,"glb");
        if (File.Exists(path) || File.Exists(model)) throw new IOException("Choose a new native face package name; existing files are preserved");
        JObject data=Capture(c);
        var document=GlbCharacterExporter.Capture(c,true);
        try { document.Write(model); } finally { document.Binary.Dispose(); }
        data["model"]=new JObject { ["file"]=System.IO.Path.GetFileName(model),["sha256"]=Hash(model) };
        File.WriteAllText(path,data.ToString(Formatting.Indented));
    }
}
