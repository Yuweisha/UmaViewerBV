using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    [DisallowMultipleComponent]
    public sealed class LiveCharacterFlareCollisionRuntime : MonoBehaviour
    {
        public sealed class PartObservation
        {
            public string Part,PrefabName,Resource,Status;
            public CharacterFlareCollisionParameter Parameter;
            public bool NativeReturn;
        }
        public sealed class BindingObservation
        {
            public string Resource;
            public int BoneIndex,CollisionIndex;
            public Collider Collider;
        }
        readonly List<PartObservation> parts=new List<PartObservation>();
        readonly List<BindingObservation> bindings=new List<BindingObservation>();
        string generation;
        Coroutine pending;
        int requestVersion;
        public bool IsReady {get;private set;}
        public Exception InstallationError {get;private set;}
        public UmaContainerCharacter Owner {get;private set;}
        public IReadOnlyList<PartObservation> Parts=>parts;
        public IReadOnlyList<BindingObservation> Bindings=>bindings;
#if UNITY_EDITOR
        // Editor observers receive the final hierarchy after deferred source-bone
        // destruction. This callback has no effect on production installation.
        public static event Action<LiveCharacterFlareCollisionRuntime> AuditReady;
#endif
        static string Generation(UmaContainerCharacter owner)
        {
            GameObject head=owner.IsMob?owner.Hair:owner.Head;
            return (owner.Body?owner.Body.GetInstanceID():0)+"/"+(head?head.GetInstanceID():0)+"/"+(owner.Tail?owner.Tail.GetInstanceID():0);
        }
        // Production entry. The source skeleton merge uses deferred Destroy;
        // no bone lookup may run until that frame has ended.
        public static LiveCharacterFlareCollisionRuntime Schedule(UmaContainerCharacter owner,
            Func<string,CharacterFlareCollisionParameter> parameterResolver=null)
        {
            var runtime=owner.GetComponent<LiveCharacterFlareCollisionRuntime>();
            if(runtime==null)runtime=owner.gameObject.AddComponent<LiveCharacterFlareCollisionRuntime>();
            if(runtime.Owner==owner&&runtime.generation==Generation(owner)&&(runtime.pending!=null||runtime.IsReady))return runtime;
            runtime.DisposeOwned();runtime.Owner=owner;runtime.generation=Generation(owner);
            int version=++runtime.requestVersion;
            runtime.pending=runtime.StartCoroutine(runtime.InstallAfterMerge(version,parameterResolver));return runtime;
        }
        IEnumerator InstallAfterMerge(int version,Func<string,CharacterFlareCollisionParameter> resolver)
        {
            yield return null; // Unity has now processed the merge frame's Destroy queue.
            if(version!=requestVersion)yield break;
            try
            {
                InstallPart("body",Owner.Body,resolver);InstallPart("head",Owner.IsMob?Owner.Hair:Owner.Head,resolver);InstallPart("tail",Owner.Tail,resolver);
                Physics.SyncTransforms();IsReady=true;
            }
            catch(Exception e){InstallationError=e;Debug.LogException(e);}
            finally{pending=null;}
#if UNITY_EDITOR
            if(IsReady)AuditReady?.Invoke(this);
#endif
        }
        public static IEnumerator WaitForReady(IList<UmaContainerCharacter> owners)
        {
            for(int i=0;i<owners.Count;i++)
            {
                var owner=owners[i];if(owner==null||!owner.IsLive||owner.IsMini)continue;
                var runtime=owner.GetComponent<LiveCharacterFlareCollisionRuntime>();
                while(!runtime.IsReady)
                {
                    if(runtime.InstallationError!=null)throw new InvalidOperationException("Original flare collider installation failed",runtime.InstallationError);
                    if(!runtime.isActiveAndEnabled||runtime.pending==null)throw new InvalidOperationException("Flare collision installation stopped before ready");
                    yield return null;
                }
            }
        }
        // Allows Builder's Action callback to retain its existing loader shape:
        // StartCoroutine(AfterReady(actors, () => { initialize timeline; play; }));
        public static IEnumerator AfterReady(IList<UmaContainerCharacter> owners,Action continuation)
        {yield return WaitForReady(owners);continuation?.Invoke();}

        // Synchronous entry only for an explicitly supplied final hierarchy/API
        // fixture. Production calls Schedule and waits at the playback barrier.
        public static LiveCharacterFlareCollisionRuntime Install(UmaContainerCharacter owner,
            Func<string,CharacterFlareCollisionParameter> parameterResolver=null)
        {
            var runtime=owner.GetComponent<LiveCharacterFlareCollisionRuntime>();
            if(runtime==null)runtime=owner.gameObject.AddComponent<LiveCharacterFlareCollisionRuntime>();
            runtime.Initialize(owner,parameterResolver);return runtime;
        }

        static string SourceName(GameObject source)=>source==null?null:source.name.Replace("(Clone)",string.Empty);
        public static string ParameterPath(string part,string prefabName)
        {
            if(string.IsNullOrEmpty(prefabName)||!prefabName.StartsWith("pfb_",StringComparison.Ordinal))return null;
            string stem=prefabName.Substring(4);string[] tokens=stem.Split('_');
            if(tokens.Length<2)return null;
            string prefix=part=="body"?"bdy":part=="head"?"chr":part=="tail"?"tail":null;
            if(prefix==null||!tokens[0].StartsWith(prefix,StringComparison.Ordinal))return null;
            string folder=tokens[0]+"_"+tokens[1];
            return "3d/chara/"+part+"/"+folder+"/flares/ast_"+stem+"_flare";
        }
        void Initialize(UmaContainerCharacter owner,Func<string,CharacterFlareCollisionParameter> parameterResolver)
        {
            GameObject head=owner.IsMob?owner.Hair:owner.Head;
            string stamp=Generation(owner);
            if(Owner==owner&&generation==stamp)return;
            DisposeOwned();Owner=owner;generation=stamp;
            InstallPart("body",owner.Body,parameterResolver);InstallPart("head",head,parameterResolver);InstallPart("tail",owner.Tail,parameterResolver);
            IsReady=true;
        }
        void InstallPart(string part,GameObject source,Func<string,CharacterFlareCollisionParameter> parameterResolver)
        {
            var row=new PartObservation{Part=part,PrefabName=SourceName(source),Status="missing-part"};parts.Add(row);
            if(source==null)return;
            row.Resource=ParameterPath(part,row.PrefabName);
            if(row.Resource==null){row.Status="unsupported-part-identity";return;}
            if(parameterResolver!=null)row.Parameter=parameterResolver(row.Resource);
            else
            {
                var main=UmaViewerMain.Instance;
                if(main==null || main.AbList==null || !main.AbList.TryGetValue(row.Resource,out var entry))
                {row.Status="missing-index";return;}
                row.Parameter=entry.Get<CharacterFlareCollisionParameter>();
            }
            if(row.Parameter==null){row.Status="missing-typed-parameter";Debug.LogError("[FlareCollision] Cannot load original parameter "+row.Resource);return;}
            row.NativeReturn=CharacterFlareCollision.AddCollisionToObject(Owner.gameObject,row.Parameter,(collider,bi,ci)=>
                bindings.Add(new BindingObservation{Resource=row.Resource,BoneIndex=bi,CollisionIndex=ci,Collider=collider}));
            row.Status=row.NativeReturn?"installed-original-parameter":"invalid-parameter-array";
        }
        public void DisposeOwned()
        {
            requestVersion++;if(pending!=null){StopCoroutine(pending);pending=null;}
            IsReady=false;InstallationError=null;
            foreach(var binding in bindings)
            {
                var collider=binding.Collider;if(collider==null)continue;
                collider.enabled=false;
#if UNITY_EDITOR
                if(!Application.isPlaying)DestroyImmediate(collider);else Destroy(collider);
#else
                Destroy(collider);
#endif
            }
            bindings.Clear();parts.Clear();generation=null;
        }
        void OnDestroy(){DisposeOwned();}
    }
}
