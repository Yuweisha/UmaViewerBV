using System.Collections.Generic;
using Gallop.Cyalume;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class StageController
    {
        readonly Dictionary<Renderer,CyalumeControllerBase> cyalumeRendererOwners = new Dictionary<Renderer,CyalumeControllerBase>();

        // Authored Object keys address generated cyalume/mob children by name.
        // Our binder creates them after the stage work table was collected,
        // so register the actual targets when they become available.
        internal void RegisterCyalumeObject(GameObject instance,CyalumeControllerBase owner)
        {
            foreach(var node in instance.GetComponentsInChildren<Transform>(true))
            {
                int hash=FNVHash.Generate(node.name.Replace("(Clone)",""));
                bool registered=false;
                if(_objectsWorkInfoDictionary.TryGetValue(hash,out var works))
                    foreach(var work in works)if(work.transform==node){registered=true;break;}
                if(!registered)RegisterObjectTransform(node,string.Empty);
                foreach(var renderer in node.GetComponents<Renderer>())cyalumeRendererOwners[renderer]=owner;
            }
        }

        internal void UnregisterCyalumeObject(GameObject instance,CyalumeControllerBase owner)
        {
            foreach(var node in instance.GetComponentsInChildren<Transform>(true))
            {
                foreach(var renderer in node.GetComponents<Renderer>())
                {cyalumeRendererOwners.Remove(renderer);owner.ForgetStageVisibility(renderer);}
                UnregisterObjectTransform(node,string.Empty);
            }
        }

        void SetStageRendererEnabled(Renderer renderer,bool value)
        {
            if(cyalumeRendererOwners.TryGetValue(renderer,out var owner)&&owner!=null)
                owner.SetStageVisibility(renderer,value);
            else renderer.enabled=value;
        }
    }
}
