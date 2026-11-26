using System;
using System.Collections.Generic;
using System.Linq;
using AnimateToUnity;
using Gallop;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

internal sealed class GachaPreviewFlash : IDisposable
{
    // UIManager.GetCameraSizeByOrientation uses 5 for portrait. Flash meshes
    // scale with the canvas, but local-scale particles are authored in UI world units.
    private const float CameraSize = 5f;
    private const float ReferenceHeight = 1920f;
    private const float CommentSampleStep = GachaCutInPlayer.PlaybackStep;
    private const float CommentTextInTime = .5f;
    private const float CommentParticleInTime = .15f;
    private const float NameFlashFadeTime = .6f;
    private const float NameFlashHideTime = 19f / 30f;
    private const float NameFlashInitialAlpha = 130f / 255f;
    private static readonly int ParticleColorId = Shader.PropertyToID("_Color");
    private static readonly int CardBackgroundLayer = LayerMask.NameToLayer("B");
    // GachaMainView.FlashRootArray[FLASH_CHARA_NAME] anchors Flash_CharaName
    // at bottom center with an anchored Y of 420 in the reference canvas.
    private const float NamePlateBottom = 420f;
    private static readonly int CommentIn = Animator.StringToHash("chara_comment_in");
    private static readonly int CommentOut = Animator.StringToHash("chara_comment_out");
    private readonly Camera owner;
    private readonly Camera camera;
    private readonly Gallop.RenderPipeline.CameraData shaderClock;
    private float elapsedTime;
    private readonly Camera cardBackgroundCamera;
    private readonly FlashPlayer comment;
    private readonly FlashPlayer namePlate;
    private readonly FlashPlayer support;
    private readonly Texture2D localizedComment;
    private readonly Texture2D localizedName;
    private readonly string supportLabelSuffix;
    private readonly AnObjectInstance commentEffectLocator;
    private readonly Canvas commentCanvas;
    private readonly Canvas nameEffectCanvas;
    private readonly Transform nameEffectParticlesRoot;
    private readonly Image nameEffectWhite;
    private readonly ParticleSystem[] nameEffectParticles = Array.Empty<ParticleSystem>();
    private double nameEffectTime;
    private readonly Animator commentAnimator;
    private readonly Image commentWhite;
    private readonly ParticleSystem[] commentParticles = Array.Empty<ParticleSystem>();
    private readonly bool[] commentEmitterActive = Array.Empty<bool>();
    private readonly AnObjectInstance[] commentText = Array.Empty<AnObjectInstance>();
    private readonly ParticleSystem horseshoes;
    private readonly ParticleSystemRenderer horseshoeRenderer;
    private readonly MaterialPropertyBlock horseshoeProperties;
    private double commentPhaseTime;
    private FlashPlayer active;
    private readonly Dictionary<FlashPlayer, FlashActionPlayer> actions = new Dictionary<FlashPlayer, FlashActionPlayer>();
    private readonly HashSet<ParticleSystem> sampledActionParticles = new HashSet<ParticleSystem>();
    private bool commentExiting;
    private bool commentExitSampled;
    private Color heldCommentFade;

    internal bool UsesCamera(Camera value) => value == camera || value == cardBackgroundCamera;

    public bool NameVisible => active != null && active == namePlate;
    public bool CommentVisible => active != null && active == comment;
    public bool LocalizedNameVisible => localizedName && NameVisible;
    public bool LocalizedCommentVisible => localizedComment && CommentVisible;
    public bool SupportVisible => active != null && active == support;
    public Color CommentFade => commentExiting && commentWhite && commentWhite.isActiveAndEnabled
        ? commentWhite.color : heldCommentFade;

    internal sealed class PlaybackState
    {
        internal bool Comment, Name, Exiting, ExitSampled, CommentCanvas, NameCanvas;
        internal float Elapsed, AnimatorTime, RootTime;
        internal double PhaseTime, NameTime;
        internal Color Fade;
        internal (string Label, float Time, int State)[] Motions;
        internal float[] CommentParticles, NameParticles;
        internal float[][] ActionParticles;
    }

    internal void EnableSeeking()
    {
        foreach (var action in actions.Values)
            action.SetKeepInstanceAll(true);
    }

    internal PlaybackState CapturePlayback()
    {
        return new PlaybackState
        {
            Comment = CommentVisible, Name = NameVisible, Exiting = commentExiting,
            ExitSampled = commentExitSampled, Elapsed = elapsedTime, PhaseTime = commentPhaseTime,
            NameTime = nameEffectTime, Fade = heldCommentFade,
            RootTime = active?.Root.Clock.CurrentTime ?? 0f,
            CommentCanvas = commentCanvas && commentCanvas.gameObject.activeSelf,
            NameCanvas = nameEffectCanvas && nameEffectCanvas.gameObject.activeSelf,
            AnimatorTime = commentAnimator && commentAnimator.isActiveAndEnabled
                ? commentAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f,
            Motions = active?.Root.Motions.Select(motion =>
                (motion.Clock.LabelName, motion.Clock.CurrentTime, motion.Clock.State)).ToArray(),
            CommentParticles = CaptureParticles(commentParticles),
            NameParticles = CaptureParticles(nameEffectParticles),
            ActionParticles = active != null && actions.TryGetValue(active, out var action)
                ? action.Actions.Select(parameter => CaptureParticles(parameter.ParticleArray)).ToArray() : null
        };
    }

    private static float[] CaptureParticles(ParticleSystem[] particles) =>
        particles?.Select(particle => particle && particle.gameObject.activeInHierarchy ? particle.time : -1f).ToArray();

    private static void RestoreParticles(ParticleSystem[] particles, float[] times)
    {
        if (particles == null)
            return;
        for (int index = 0; index < particles.Length; index++)
        {
            var particle = particles[index];
            if (!particle)
                continue;
            float age = times != null && index < times.Length ? times[index] : -1f;
            particle.Simulate(Mathf.Max(0f, age), false, true, false);
            particle.Pause(false);
        }
    }

    internal void RestorePlayback(PlaybackState state)
    {
        Clear();
        if (state.Comment)
            PlayComment("in");
        else if (state.Name)
            PlayName();
        if (active != null && state.Motions != null)
        {
            active.Root.Clock.CurrentTime = state.RootTime;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int index = 0; index < active.Root.Motions.Count; index++)
                {
                    var motion = active.Root.Motions[index];
                    var saved = state.Motions[index];
                    motion.Clock.Play(saved.Label, saved.Time -
                        motion.Parameter._labelParamList.First(label => label._name == saved.Label)._timeRange.x,
                        saved.State != 0);
                }
                active.Root.Evaluate(0f);
            }
        }
        elapsedTime = state.Elapsed;
        commentPhaseTime = state.PhaseTime;
        nameEffectTime = state.NameTime;
        commentExiting = state.Exiting;
        commentExitSampled = state.ExitSampled;
        heldCommentFade = state.Fade;
        if (commentCanvas)
            commentCanvas.gameObject.SetActive(state.CommentCanvas);
        if (nameEffectCanvas)
            nameEffectCanvas.gameObject.SetActive(state.NameCanvas);
        if (commentAnimator && state.CommentCanvas)
        {
            commentAnimator.speed = 1f;
            commentAnimator.Play(state.Exiting ? CommentOut : CommentIn, 0, state.AnimatorTime);
            commentAnimator.Update(0f);
            commentAnimator.speed = 0f;
        }
        UpdateCommentText((float)commentPhaseTime);
        RestoreParticles(commentParticles, state.CommentParticles);
        RestoreParticles(nameEffectParticles, state.NameParticles);
        for (int index = 0; index < commentParticles.Length; index++)
            commentEmitterActive[index] = state.CommentParticles[index] >= 0f;
        if (active != null && actions.TryGetValue(active, out var action))
            for (int index = 0; index < action.Actions.Count; index++)
            {
                var particles = action.Actions[index].ParticleArray;
                var times = state.ActionParticles?[index];
                if (action.Actions[index].InstantiatePrefabInstance)
                    action.Actions[index].InstantiatePrefabInstance.SetActive(times?.Any(age => age >= 0f) == true);
                RestoreParticles(particles, times);
                if (particles != null)
                    foreach (var particle in particles.Where(particle => particle))
                        sampledActionParticles.Add(particle);
            }
        if (nameEffectWhite)
        {
            nameEffectWhite.gameObject.SetActive(nameEffectTime < NameFlashHideTime);
            SampleNameEffect(0f);
        }
        shaderClock.CutInShaderTime = elapsedTime;
        if (cardBackgroundCamera)
        {
            var clock = cardBackgroundCamera.GetComponent<Gallop.RenderPipeline.CameraData>();
            if (clock)
                clock.CutInShaderTime = elapsedTime;
        }
    }

    private GachaPreviewFlash(Camera owner)
    {
        this.owner = owner;
        var node = new GameObject("Gacha Flash camera");
        node.transform.SetParent(owner.transform.parent, false);
        node.transform.localPosition = new Vector3(0f, 0f, -3000f);
        camera = node.AddComponent<Camera>();
        shaderClock = node.AddComponent<Gallop.RenderPipeline.CameraData>();
        shaderClock.CutInShaderTime = 0f;
        camera.orthographic = true;
        camera.orthographicSize = CameraSize;
        camera.clearFlags = CameraClearFlags.Depth;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 30f;
        camera.aspect = owner.aspect;
        camera.cullingMask = 1 << LayerMask.NameToLayer("UI");
        camera.allowHDR = false;
        camera.useOcclusionCulling = false;
        var settings = camera.GetUniversalAdditionalCameraData();
        settings.renderType = CameraRenderType.Overlay;
        settings.renderPostProcessing = false;
        settings.renderShadows = false;
        owner.GetUniversalAdditionalCameraData().cameraStack.Add(camera);
    }

    public GachaPreviewFlash(Camera owner, GameObject commentPrefab, Texture2D commentTexture, GameObject commentEffect,
        GameObject namePrefab, Texture2D nameTexture, GameObject nameEffect, Material nameEffectMaterial,
        string cardTitle, Func<string, GameObject> loadPrefab, Font font, Texture2D localizedComment,
        Texture2D localizedName) : this(owner)
    {
        this.localizedComment = localizedComment;
        this.localizedName = localizedName;
        try
        {
            comment = Create(commentPrefab, localizedComment ? localizedComment : commentTexture,
                "PLN_dum_comment00", loadPrefab, characterEffects: true, fitTextureWidth: true);
            namePlate = Create(namePrefab, localizedName ? localizedName : nameTexture,
                "PLN_dum_charaname00", loadPrefab, NamePlateBottom, characterEffects: true);
            if (namePlate != null && font)
                foreach (string name in namePlate.Root.Objects.Where(node => node.Text != null)
                    .Select(node => node.Text.Parameter._fontName).Distinct())
                    namePlate.ReplaceFont(name, font);
            namePlate?.SetText(cardTitle, "TXT_txt_chara_costume00", true, null);
            if (namePlate != null && nameEffect)
            {
                nameEffectCanvas = CreateEffectCanvas(nameEffect, "Gacha name effect");
                nameEffectParticlesRoot = nameEffectCanvas.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(node => node.name == "pfb_uieff_gacha_chara_complete00");
                if (nameEffectMaterial)
                {
                    nameEffectWhite = nameEffectCanvas.GetComponentsInChildren<Image>(true)
                        .FirstOrDefault(image => image.name == "white_plate");
                    if (nameEffectWhite)
                        nameEffectWhite.material = nameEffectMaterial;
                }
                if (nameEffectParticlesRoot)
                {
                    nameEffectParticles = nameEffectParticlesRoot.GetComponentsInChildren<ParticleSystem>(true);
                    PrepareCharacterParticles(nameEffectParticles);
                    foreach (var particle in nameEffectParticles)
                    {
                        var main = particle.main;
                        main.playOnAwake = false;
                    }
                }
                nameEffectCanvas.gameObject.SetActive(false);
            }
            commentEffectLocator = comment?.Root.FindObject("OBJ_loc_unity_effect");
            if (comment != null)
                commentText = comment.Root.Objects
                    .Where(node => node.Plane != null && node.Transform.name == "PLN_dum_comment00").ToArray();
            if (commentEffect && commentEffectLocator != null)
            {
                commentCanvas = CreateEffectCanvas(commentEffect, "Gacha comment effect");
                commentParticles = commentCanvas.GetComponentsInChildren<ParticleSystem>(true);
                commentEmitterActive = new bool[commentParticles.Length];
                horseshoes = Array.Find(commentParticles, particle => particle.name == "horseshoe_w00_add");
                if (horseshoes)
                {
                    horseshoeRenderer = horseshoes.GetComponent<ParticleSystemRenderer>();
                    horseshoeProperties = new MaterialPropertyBlock();
                }
                PrepareCharacterParticles(commentParticles);
                // The effect Animator toggles child objects. Start and step each
                // emitter at that sampled transition, outside Unity's auto update.
                foreach (var particle in commentParticles)
                {
                    var main = particle.main;
                    main.playOnAwake = false;
                }
                commentWhite = Array.Find(commentCanvas.GetComponentsInChildren<Image>(true),
                    image => image.name == "Img_white");
                if (commentWhite)
                {
                    // Sample the authored Image on the viewer's presentation surface.
                    // Leaving the component enabled keeps the Animator binding intact.
                    commentWhite.canvasRenderer.SetAlpha(0f);
                }
                commentAnimator = commentCanvas.GetComponentInChildren<Animator>(true);
                if (commentAnimator)
                {
                    commentAnimator.updateMode = AnimatorUpdateMode.Normal;
                    commentAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    commentAnimator.speed = 0f;
                }
                commentCanvas.gameObject.SetActive(false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public GachaPreviewFlash(Camera owner, GameObject prefab, Texture2D art, Texture2D name, string title,
        int rarity, Camera photoCamera, Func<string, GameObject> loadPrefab) : this(owner)
    {
        try
        {
            support = Create(prefab, art, "PLN_dum_card00", loadPrefab);
            foreach (var node in support.Root.Objects)
            {
                if (node.Transform.name == "PLN_dum_card01")
                    node.Plane?.SetTexture(art);
                else if (node.Transform.name == "PLN_dum_charaname00")
                    node.Plane?.SetTexture(name);
            }
            support.SetText(title, "TXT_txt_card_subtitle00", true, null);
            supportLabelSuffix = rarity == 3 ? "02" : "";
            var back = support.Root.FindMotion("MOT_mc_gacha_supportcard_back").Transform.gameObject;
            GachaCutInLoader.SetLayer(back, CardBackgroundLayer);
            cardBackgroundCamera = new GameObject("Gacha card background camera").AddComponent<Camera>();
            cardBackgroundCamera.transform.SetParent(camera.transform, false);
            cardBackgroundCamera.CopyFrom(camera);
            cardBackgroundCamera.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            cardBackgroundCamera.cullingMask = 1 << CardBackgroundLayer;
            var settings = cardBackgroundCamera.GetUniversalAdditionalCameraData();
            settings.renderType = CameraRenderType.Overlay;
            settings.renderPostProcessing = false;
            settings.renderShadows = false;
            var stack = owner.GetUniversalAdditionalCameraData().cameraStack;
            stack.Insert(stack.IndexOf(photoCamera), cardBackgroundCamera);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private Canvas CreateEffectCanvas(GameObject prefab, string name)
    {
        var canvas = new GameObject(name, typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        var rect = (RectTransform)canvas.transform;
        rect.SetParent(camera.transform, false);
        rect.localPosition = Vector3.forward * 10f;
        rect.localScale = Vector3.one * (2f * CameraSize / ReferenceHeight);
        rect.sizeDelta = new Vector2(ReferenceHeight * camera.aspect, ReferenceHeight);
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        canvas.sortingLayerName = "UI";
        var effect = Object.Instantiate(prefab, rect, false);
        GachaCutInPlayer.ReplayResources.SeedParticles(effect);
        int layer = LayerMask.NameToLayer("UI");
        foreach (var child in rect.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
        return canvas;
    }

    private static void PrepareCharacterParticles(ParticleSystem[] particles)
    {
        foreach (var particle in particles)
        {
            var main = particle.main;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            // Rebind the serialized sorting layer to the viewer's Flash layer.
            // Keep each renderer's authored order relative to the text and background.
            var renderer = particle.GetComponent<ParticleSystemRenderer>();
            if (renderer)
                renderer.sortingLayerName = "UI";
        }
    }

    private FlashPlayer Create(GameObject prefab, Texture2D texture, string planeName,
        Func<string, GameObject> loadPrefab, float? bottom = null, bool characterEffects = false, bool fitTextureWidth = false)
    {
        if (!prefab || !texture)
            return null;
        var instance = Object.Instantiate(prefab, camera.transform, false);
        GachaCutInPlayer.ReplayResources.SeedParticles(instance);
        var action = instance.GetComponent<FlashActionPlayer>();
        if (action)
        {
            action.LoadFlashPlayer(loadPrefab);
            if (characterEffects)
                action.OnInstantiatedParticles = particles =>
                {
                    foreach (var parameter in action.Actions)
                        if (parameter.InstantiatePrefabInstance && particles.Any(particle =>
                            particle && particle.transform.IsChildOf(parameter.InstantiatePrefabInstance.transform)))
                            parameter.InstantiatePrefabInstance.SetActive(true);
                    foreach (var particle in particles)
                        GachaCutInPlayer.ReplayResources.SeedParticles(particle.gameObject);
                    PrepareCharacterParticles(particles);
                    foreach (var particle in particles)
                        if (particle.useAutoRandomSeed)
                        {
                            uint seed = particle.randomSeed;
                            particle.useAutoRandomSeed = false;
                            particle.randomSeed = seed;
                        }
                };
        }
        var flash = action ? action.FlashPlayer : new FlashPlayer(instance);
        if (action)
            actions.Add(flash, action);
        var referenceSize = flash.Root._parameter._baseScreenFixSize;
        if (referenceSize.x <= 0f || referenceSize.y <= 0f)
            referenceSize = flash.Root._parameter._baseScreenSize;
        var viewport = new Vector2(referenceSize.y * owner.aspect, referenceSize.y);
        float heightScale = 2f * CameraSize / referenceSize.y;
        float nameFit = bottom.HasValue ? Mathf.Min(1f, viewport.x / referenceSize.x) : 1f;
        float centerY = bottom.HasValue ? (bottom.Value - 0.5f * referenceSize.y) * heightScale : 0f;
        instance.transform.localPosition = new Vector3(0f, centerY, 10f);
        instance.transform.localScale = Vector3.one * (heightScale * nameFit);
        flash.Root.SetViewportSize(viewport / nameFit);
        float textureFit = fitTextureWidth ? Mathf.Min(1f, viewport.x / referenceSize.x) : 1f;
        foreach (var node in flash.Root.Objects)
            if (node.Transform.name == planeName)
                node.Plane?.SetTexture(texture, scale: textureFit);
        flash.SetVisible(false);
        return flash;
    }

    public bool PlayComment(string label)
    {
        if (!Play(comment, label))
            return false;
        commentPhaseTime = 0;
        UpdateCommentText(0f);
        Evaluate(comment, 0f);
        SampleCommentEffect(0f);
        return true;
    }

    public bool PlayName()
    {
        if (!Play(namePlate, "in"))
            return false;
        if (nameEffectCanvas)
        {
            nameEffectTime = 0;
            nameEffectCanvas.gameObject.SetActive(true);
            if (nameEffectParticlesRoot)
                nameEffectParticlesRoot.gameObject.SetActive(true);
            foreach (var particle in nameEffectParticles)
                particle.Simulate(0f, false, true, false);
            if (nameEffectWhite)
                nameEffectWhite.gameObject.SetActive(true);
            SampleNameEffect(0f);
        }
        return true;
    }

    public void PlaySupport(bool enter)
    {
        string label = (enter ? "in" : "out") + supportLabelSuffix;
        Play(support, label);
        support.Root.FindMotion("MOT_mc_gacha_supportcard_top")?.Play(label);
        support.Root.FindMotion("MOT_mc_gacha_supportcard_back")?.Play(label);
    }

    public void EndComment()
    {
        if (CommentVisible)
        {
            commentExiting = comment.Root.RootMotion.Play("out");
            commentPhaseTime = 0;
            Evaluate(comment, 0f);
            if (commentAnimator && commentAnimator.isActiveAndEnabled)
            {
                commentAnimator.Play(CommentOut, 0, 0f);
                SampleCommentEffect(0f);
                commentExiting = true;
            }
        }
    }

    private bool Play(FlashPlayer flash, string label)
    {
        if (flash == null || string.IsNullOrEmpty(label))
            return false;
        if (active != flash)
        {
            Clear();
            active = flash;
            flash.SetVisible(true);
        }
        return flash.Root.RootMotion.Play(label);
    }

    public void Tick(float deltaTime)
    {
        elapsedTime += deltaTime;
        shaderClock.CutInShaderTime = elapsedTime;
        if (cardBackgroundCamera)
        {
            var clock = cardBackgroundCamera.GetComponent<Gallop.RenderPipeline.CameraData>();
            if (clock)
                clock.CutInShaderTime = elapsedTime;
        }
        if (active == null)
            return;
        if (commentExitSampled)
        {
            var terminalFade = CommentFade;
            Clear();
            // The cut clock resumes after the comment Animator finishes.
            // Keep the terminal fade until the next cut clears this Flash.
            heldCommentFade = terminalFade;
            return;
        }
        if (!CommentVisible)
        {
            Evaluate(active, deltaTime);
            if (NameVisible && nameEffectCanvas)
                SampleNameEffect(deltaTime);
            return;
        }
        int steps = Mathf.Max(1, Mathf.CeilToInt(deltaTime / CommentSampleStep));
        float step = deltaTime / steps;
        for (int index = 0; index < steps; index++)
        {
            commentPhaseTime += step;
            UpdateCommentText((float)commentPhaseTime);
            Evaluate(active, step);
            SampleCommentEffect(step);
        }
        if (commentExiting)
        {
            var clock = comment.Root.RootMotion.Clock;
            // GachaFlashCharaComment.Update resumes the cut after the effect Animator ends.
            bool finished = commentAnimator && commentAnimator.isActiveAndEnabled
                ? commentAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f
                : clock.State != 0 || clock.LabelName == "end";
            // Present the terminal fade sample before releasing the next cut.
            commentExitSampled = finished;
        }
    }

    private void Evaluate(FlashPlayer flash, float deltaTime)
    {
        flash.Root.Evaluate(deltaTime);
        if (actions.TryGetValue(flash, out var action))
        {
            if (flash != comment && flash != namePlate)
                action.UpdateParticle();
            else
                foreach (var particle in action.Actions.Where(parameter => parameter.ParticleArray != null)
                    .SelectMany(parameter => parameter.ParticleArray).Where(particle => particle).Distinct())
                    if (sampledActionParticles.Add(particle))
                        particle.Simulate(0f, false, true, false);
                    else if (deltaTime > 0f)
                        particle.Simulate(deltaTime, false, false, false);
        }
    }

    private void UpdateCommentText(float elapsed)
    {
        // Both Flash planes use the same presentation envelope. Their authored
        // color curves and the Animator's white transition remain independent.
        float alpha = commentExiting
            ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, .9f, elapsed))
            : Mathf.SmoothStep(0f, 1f, elapsed / CommentTextInTime);
        foreach (var node in commentText)
        {
            var color = node.State.MultiplyColor;
            color.a = alpha;
            node.State.MultiplyColor = color;
        }
    }

    private void SampleNameEffect(float deltaTime)
    {
        nameEffectTime += deltaTime;
        if (deltaTime > 0f)
            foreach (var particle in nameEffectParticles)
                particle.Simulate(deltaTime, false, false, false);
        if (nameEffectWhite && nameEffectWhite.gameObject.activeSelf)
        {
            float remaining = 1f - Mathf.Clamp01((float)nameEffectTime / NameFlashFadeTime);
            nameEffectWhite.canvasRenderer.SetAlpha(NameFlashInitialAlpha * remaining * remaining);
            if (nameEffectTime >= NameFlashHideTime)
                nameEffectWhite.gameObject.SetActive(false);
        }
    }

    private void SampleCommentEffect(float deltaTime)
    {
        if (!commentCanvas)
            return;
        if (!commentCanvas.gameObject.activeSelf)
        {
            if (commentExiting || !commentEffectLocator.VisibleInHierarchy)
                return;
            commentCanvas.gameObject.SetActive(true);
            if (commentAnimator)
                commentAnimator.Play(CommentIn, 0, 0f);
            deltaTime = 0f;
        }

        if (commentAnimator && commentAnimator.isActiveAndEnabled)
        {
            commentAnimator.speed = 1f;
            try { commentAnimator.Update(deltaTime); }
            finally { commentAnimator.speed = 0f; }
        }

        StepCommentParticles(deltaTime);
        if (horseshoeRenderer && horseshoes.gameObject.activeInHierarchy)
        {
            // The authored 0.3s burst stays in place. Fade its contribution
            // using emitter time, which excludes the authored start delay.
            horseshoeRenderer.GetPropertyBlock(horseshoeProperties);
            float alpha = Mathf.SmoothStep(0f, 1f, horseshoes.time / CommentParticleInTime);
            horseshoeProperties.SetColor(ParticleColorId, new Color(1f, 1f, 1f, alpha));
            horseshoeRenderer.SetPropertyBlock(horseshoeProperties);
        }
    }

    private void StepCommentParticles(float deltaTime)
    {
        for (int index = 0; index < commentParticles.Length; index++)
        {
            var particle = commentParticles[index];
            bool enabled = particle && particle.gameObject.activeInHierarchy;
            bool wasEnabled = commentEmitterActive[index];
            commentEmitterActive[index] = enabled;
            if (!enabled)
                continue;
            if (!wasEnabled)
                particle.Simulate(0f, false, true, false);
            else if (deltaTime > 0f)
                particle.Simulate(deltaTime, false, false, false);
        }
    }

    public void Clear()
    {
        sampledActionParticles.Clear();
        active?.SetVisible(false);
        active = null;
        if (commentCanvas)
            commentCanvas.gameObject.SetActive(false);
        if (nameEffectCanvas)
            nameEffectCanvas.gameObject.SetActive(false);
        Array.Clear(commentEmitterActive, 0, commentEmitterActive.Length);
        commentExiting = false;
        commentExitSampled = false;
        heldCommentFade = Color.clear;
    }

    public void Dispose()
    {
        if (!camera)
            return;
        var stack = owner.GetUniversalAdditionalCameraData().cameraStack;
        if (cardBackgroundCamera)
        {
            stack.Remove(cardBackgroundCamera);
            cardBackgroundCamera.enabled = false;
        }
        stack.Remove(camera);
        camera.enabled = false;
        Object.Destroy(camera.gameObject);
        Object.Destroy(localizedComment);
        Object.Destroy(localizedName);
    }
}
