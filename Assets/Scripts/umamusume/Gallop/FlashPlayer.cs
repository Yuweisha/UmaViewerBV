using System;
using System.Collections.Generic;
using AnimateToUnity;
using Gallop.Live;
using UnityEngine;

namespace Gallop
{
    public sealed class FlashPlayer
    {
        public readonly AnRoot Root;
        readonly Dictionary<string,Font> fonts=new Dictionary<string,Font>();
        static readonly Dictionary<string,Font> overrides=new Dictionary<string,Font>();
        public static void RegisterFont(string name,Font font){if(font==null)overrides.Remove(name);else overrides[name]=font;}
        public FlashPlayer(GameObject instance)
        {
            Root=instance.GetComponentInChildren<AnRoot>(true);
            if(Root==null)throw new InvalidOperationException("Flash prefab has no AnRoot: "+instance.name);
            Root.FontResolver=ResolveFont;Root.Initialize();
        }
        Font ResolveFont(string name)
        {
            if(fonts.TryGetValue(name,out var font))return font;
            font=overrides.TryGetValue(name,out var replacement)?replacement:Resources.Load<Font>("Fonts/LiveTitle-Dynamic01");
            fonts.Add(name,font);return font;
        }
        public void Pause(bool pause)=>Root.Paused=pause;
        // Native FlashPlayer.SetScale 0x4c24fd0: scale the AnRoot transform.
        public void SetScale(Vector3 scale){if(Root!=null)Root.transform.localScale=scale;}
        // Native FlashPlayer.SetVisible 0x4c258d0.
        public void SetVisible(bool visible){if(Root!=null)Root.gameObject.SetActive(visible);}
        public void AlterUpdate()=>Root.Evaluate(Time.deltaTime);
        public void SetCurrentTimeAllMotion(float time)=>Root.SetCurrentTimeAll(time);
        public GameObject GetObj(string name,bool unused0=false,bool unused1=false,bool unused2=false)
        {var node=Root.FindObject(name);return node!=null?node.Transform.gameObject:Root.FindMotion(name)?.Transform.gameObject;}
        public bool SetText(string text,string name,bool recursive,string rootName)
        {
            var search=string.IsNullOrEmpty(rootName)?null:GetObj(rootName)?.transform;
            bool changed=false;
            foreach(var node in Root.Objects)
                if(node.Text!=null&&AnRoot.MatchesObject(node,name)&&(search==null||node.Transform.IsChildOf(search)))
                {node.Text.SetText(text);changed=true;}
            return changed;
        }
        public bool SetTexture(string name,Texture texture,int index,bool keepSize,bool unused1,string rootName,int unused2=0)
        {
            var search=string.IsNullOrEmpty(rootName)?null:GetObj(rootName)?.transform;
            var node=Root.FindObject(name,search);if(node?.Plane==null)return false;node.Plane.SetTexture(texture,keepSize);return true;
        }
        public void ReplaceFont(string name,Font font)
        {if(font==null)return;fonts[name]=font;foreach(var node in Root.Objects)if(node.Text?.Parameter._fontName==name)node.Text.SetFont(font);}
        public void Reset(){foreach(var motion in Root.Motions)motion.Play(motion.Parameter._labelParamList[0]._name);Root.Evaluate(0f);}
    }
}
