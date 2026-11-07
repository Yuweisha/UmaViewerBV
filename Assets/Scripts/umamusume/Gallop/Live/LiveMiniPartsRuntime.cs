using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // CharaPartsHolder.InitializeMiniMaterialFromRenderer 0x51684d0.
    // RendererHolder's already-instanced material arrays are explicit input.
    // Full RendererHolder initialization/material defaults and Cheek ownership
    // are handled by their own owners, not inferred from a renderer heuristic.
    public sealed class LiveMiniPartsRuntime
    {
        public Renderer FaceRenderer, HairRenderer, BodyRenderer, TailRenderer;
        public Renderer MayuLRenderer, MayuRRenderer, EyeRenderer, MouthRenderer, AttachmentRenderer;
        public Material FaceSkinMaterial, HairMaterial, TailMaterial, MayuLMaterial, MayuRMaterial;
        public Material EyeMaterial, MouthMaterial, AttachmentMaterial, JawMaterial;
        public Material[] BodyMaterialArray, BodyPropMaterialArray, FacePropMaterialArray, HairPropMaterialArray;
        public Material[] HeadMaterialArray, HeadObjMaterialArray;
        public Renderer[] BodyPropRendererArray, FacePropRendererArray, HairPropRendererArray;
        public bool IsFace, IsHair, IsBody, IsTail, IsMayuL, IsMayuR, IsEye, IsMouth, IsAttachment;
        public bool IsBodyProp, IsFaceProp, IsHairProp, IsHead, IsHeadObj, IsJaw;
        public readonly List<Material> MaterialList = new List<Material>();
#if UNITY_EDITOR
        public event Action<Material,int> QueueWrite;
#endif
        public void Initialize(Renderer[] renderers, Material[][] materials)        {
            var bodyRenderers = new List<Renderer>(); var bodyMaterials = new List<Material>();
            var faceRenderers = new List<Renderer>(); var faceMaterials = new List<Material>();
            var hairRenderers = new List<Renderer>(); var hairMaterials = new List<Material>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i]; string name = renderer.name;
                if (BodyRenderer == null && name == "M_Body")
                { BodyRenderer=renderer;BodyMaterialArray=materials[i];MaterialList.AddRange(BodyMaterialArray);IsBody=true;continue; }
                if (HairRenderer == null && name == "M_Hair")
                { HairRenderer=renderer;HairMaterial=First(materials[i]);MaterialList.Add(HairMaterial);IsHair=true;continue; }
                if (FaceRenderer == null && name == "M_Face")
                { FaceRenderer=renderer;FaceSkinMaterial=First(materials[i]);MaterialList.Add(FaceSkinMaterial);IsFace=true;continue; }
                if (MayuLRenderer == null && name == "M_Mayu_L")
                { MayuLRenderer=renderer;MayuLMaterial=First(materials[i]);MaterialList.Add(MayuLMaterial);IsMayuL=true;continue; }
                if (MayuRRenderer == null && name == "M_Mayu_R")
                { MayuRRenderer=renderer;MayuRMaterial=First(materials[i]);MaterialList.Add(MayuRMaterial);IsMayuR=true;continue; }
                if (EyeRenderer == null && name == "M_Eye")
                { EyeRenderer=renderer;EyeMaterial=First(materials[i]);MaterialList.Add(EyeMaterial);IsEye=true;continue; }
                if (MouthRenderer == null && name == "M_Mouth")
                { MouthRenderer=renderer;MouthMaterial=First(materials[i]);MaterialList.Add(MouthMaterial);IsMouth=true;continue; }
                if (TailRenderer == null && name == "M_Tail")
                { TailRenderer=renderer;TailMaterial=First(materials[i]);MaterialList.Add(TailMaterial);IsTail=true;continue; }
                if (AttachmentRenderer == null && name == "M_Attach")
                { AttachmentRenderer=renderer;AttachmentMaterial=First(materials[i]);MaterialList.Add(AttachmentMaterial);IsAttachment=true;continue; }
                if (name.IndexOf("M_Body_Prop")>=0)
                { bodyRenderers.Add(renderer);bodyMaterials.Add(First(materials[i]));MaterialList.Add(First(materials[i]));IsBodyProp=true; }
                else if (name.IndexOf("M_Face_Prop")>=0)
                { faceRenderers.Add(renderer);faceMaterials.Add(First(materials[i]));MaterialList.Add(First(materials[i]));IsFaceProp=true; }
                else if (name.IndexOf("M_Hair_Prop")>=0)
                { hairRenderers.Add(renderer);hairMaterials.Add(First(materials[i]));MaterialList.Add(First(materials[i]));IsHairProp=true; }
            }
            BodyPropRendererArray=bodyRenderers.ToArray();BodyPropMaterialArray=bodyMaterials.ToArray();
            FacePropRendererArray=faceRenderers.ToArray();FacePropMaterialArray=faceMaterials.ToArray();
            HairPropRendererArray=hairRenderers.ToArray();HairPropMaterialArray=hairMaterials.ToArray();
        }
        // Original specialized initializer consumes RendererHolder cache getters;
        // callers holding the initialized holder pass it here instead of supplying
        // a fixture-instanced Material[][] cache.
        public void Initialize(LiveRendererHolder holder)
        {
            if (holder == null) return;
            Initialize(holder.RendererArray, holder.MaterialArray);
        }
        static Material First(Material[] materials) => materials.Length>0 ? materials[0] : null;
        void Write(Material material,int queue)
        {
            material.renderQueue=queue;
#if UNITY_EDITOR
            QueueWrite?.Invoke(material,queue);
#endif
        }
        void Write(Material[] materials,int queue) { foreach(var material in materials) Write(material,queue); }

        // Original complete MiniModelController.UpdateRenderQueue 0x33cf910;
        // CheekController is a separately supplied consumer, never a named mesh
        // substituted for the original component's material initialization.
        public void UpdateRenderQueue(int order,bool isStencil,bool baseQueueIsZero,Action<int> cheekQueue=null)
        {
            int b=baseQueueIsZero?0:unchecked((19-order)*7);
            int body=isStencil?unchecked(1999-b):2000;
            if(IsBody)Write(BodyMaterialArray,body);
            if(IsBodyProp)Write(BodyPropMaterialArray,body);
            if(IsHair)Write(HairMaterial,unchecked(1997-b));
            if(IsHairProp)Write(HairPropMaterialArray,unchecked(1997-b));
            if(IsFace)Write(FaceSkinMaterial,unchecked(1994-b));
            if(IsFaceProp)Write(FacePropMaterialArray,unchecked(1997-b));
            if(IsMayuL)Write(MayuLMaterial,unchecked(1998-b));
            if(IsMayuR)Write(MayuRMaterial,unchecked(1998-b));
            if(IsEye)Write(EyeMaterial,unchecked(1995-b));
            if(IsMouth)Write(MouthMaterial,unchecked(1993-b));
            if(IsTail)Write(TailMaterial,body);
            cheekQueue?.Invoke(unchecked(1996-b));
            if(IsAttachment)Write(AttachmentMaterial,b);
        }
    }
}
