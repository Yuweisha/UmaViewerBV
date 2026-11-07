using System;
using System.Linq;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Live owns the model, the four animation channels and their engine peers.
    public sealed class LiveMiniCharacter : MonoBehaviour, ILiveMiniTransformPlayer
    {
        public UmaContainerCharacter Model {get;private set;}
        public LiveMiniAnimationRuntime Animation {get;private set;}
        public LiveMiniColorRuntime Color {get;private set;}
        public LiveRendererHolder RendererHolder {get;private set;}
        public LiveMiniPartsRuntime Parts {get;private set;}
        public LiveMiniCheekRuntime Cheek {get;private set;}
        LiveMiniFacialRuntime facial;
        LiveMiniEarClipRuntime ear;
        LiveMiniEarWeightRuntime earWeights;
        LiveMiniEarTargetRuntime earTarget;
        Animator facialAnimator;
        AnimatorOverrideController facialOverrides;
        Renderer[] renderers;
        public Transform RootTransform => transform;
        public float BodyScale {get=>Model.BodyScale;set=>Model.BodyScale=value;}
        public bool IsPaused=>Model.LiveModelComponentsPaused;
        public void SetPaused(bool paused)
        {
            Model.SetLiveComponentsPaused(paused);
            facial.IsPaused=paused;
        }

        static T Asset<T>(string path) where T:UnityEngine.Object
        {
            var entry=UmaViewerMain.Instance.AbList[path];
            return UmaAssetManager.LoadAssetBundle(entry).LoadAllAssets<T>().Single();
        }
        public void Initialize(UmaContainerCharacter model)
        {
            Model=model;
            RendererHolder=new LiveRendererHolder();
            RendererHolder.OnInitialize(model);
            renderers=RendererHolder.RendererArray;
            Parts=new LiveMiniPartsRuntime();
            Parts.Initialize(RendererHolder);
            Color=new LiveMiniColorRuntime(renderers);
            var locator=Instantiate(Asset<GameObject>("3d/animator/pfb_mini_facial_locator"),model.transform);
            facialAnimator=locator.GetComponent<Animator>();
            facialOverrides=new AnimatorOverrideController(Asset<RuntimeAnimatorController>("3d/animator/anm_mini_animator"));
            facialAnimator.runtimeAnimatorController=facialOverrides;
            facialAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            Material Mat(string name)=>renderers.Single(r=>r.name==name).material;
            facial=new LiveMiniFacialRuntime(Mat("M_Mayu_L"),Mat("M_Mayu_R"),Mat("M_Eye"),Mat("M_Mouth"),locator.transform,facialAnimator,facialOverrides);
            Cheek=new LiveMiniCheekRuntime(locator.transform);
            int skin=Convert.ToInt32(model.CharaData["skin"]);
            var cheek=renderers.SingleOrDefault(r=>r.name=="M_Cheek");
            Cheek.Initialize(RendererHolder,cheek!=null?cheek.gameObject:null,
                Asset<Texture2D>(LiveMiniBuildPaths.CheekTexture(13,0,skin,0).ToLowerInvariant()),
                Asset<Texture2D>(LiveMiniBuildPaths.CheekTexture(13,0,skin,1).ToLowerInvariant()));
            var earLocator=Instantiate(Asset<GameObject>("3d/animator/drivenkeylocator"),model.transform);
            var earAnimator=earLocator.GetComponent<Animator>();
            earAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            ear=new LiveMiniEarClipRuntime(earAnimator);
            earWeights=new LiveMiniEarWeightRuntime(earLocator.transform);
            var holder=model.GetComponentsInChildren<AssetHolder>(true).Single(h=>h.Get<DrivenKeyTarget>("ear_target")!=null);
            earTarget=new LiveMiniEarTargetRuntime(holder.Get<DrivenKeyTarget>("ear_target"),holder.GetValue("ear_target_scale"),model.transform);
            // Original MiniAnimationController51a83a0 adds the position Animator
            // to ModelControllerBehaviour.PositionNode (virtual slot11), while
            // the timeline transform writes the cached model transform.
            Animation=new LiveMiniAnimationRuntime(model.UmaAnimator,model.Position.gameObject.AddComponent<Animator>(),facial,ear);
            model.InitializeLiveMiniPhysics();
            SetLayerIndex(1);
        }
        public void SetVisible(bool visible)
        {
            foreach(var renderer in renderers) if(renderer!=null) renderer.enabled=visible;
        }
        public void SetLayerIndex(int index)
        {
            // Timeline values are GraphicSettings.LayerIndex, not Unity layer
            // numbers (e.g. logical CharaMini20 maps to host layer29).
            int layer=GraphicSettings.GetLayer((GraphicSettings.LayerIndex)index);
            foreach(var node in Model.GetComponentsInChildren<Transform>(true))node.gameObject.layer=layer;
        }
        public void ResetCyspringScale(int parts)=>Model.ResetLiveMiniSpringScale(parts);
        public void Evaluate(float delta)
        {
            Animation.Body.Evaluate();
            Animation.Position.Evaluate();
            facialAnimator.Update(0f);
            facial.AfterApplyDrivenKey();
            Cheek.OnUpdate(delta);
            ear.UpdateAndEvaluate(0f);
            earWeights.UpdateAnimation(ear.Animation);
            earTarget.Calculate(earWeights);
            earTarget.ApplyForce();
            Model.EvaluateLiveMiniPhysics(delta);
        }
        void OnDestroy()
        {
            Animation?.Dispose();
            ear?.Dispose();
            if(facialOverrides!=null)Destroy(facialOverrides);
        }
    }
}
