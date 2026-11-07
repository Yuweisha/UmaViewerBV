using System.Collections.Generic;
using Gallop;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // One live model's emissive sink: writes the model MaterialPropertyBlock and
    // reflects it to the model's renderer set (RendererHolder.ApplyMaterialPropertyBlock
    // 0x51b8e80 semantics). Property ids/order follow ModelController
    // 0x52a5d10/0x52a5da0/0x52a5f90: color(28), intensity(29),
    // rim power(31), rim intensity(30), center(32 via SetInt).
    public sealed class LiveModelEmissiveSink
    {
#if UNITY_EDITOR
        // Isolated A/B observation boundary; absent from player builds. Normal
        // Editor playback has no hook and executes the unchanged submission.
#endif
        private MaterialPropertyBlock _block;
        private MaterialPropertyBlock _rendererBlock;
        private int _submissionDepth;
        private bool _submissionPending;
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public readonly LiveDirtController Dirt = new LiveDirtController();
        public bool HasDirtController;
        // Formation writes all of these owned shader fields consecutively;
        // no rendering occurs between its setters. Merge the final fields into
        // each renderer once at that boundary, retaining unrelated MPB fields.
        public void BeginMaterialSubmission()
        {
            _submissionDepth++;
        }

        public void EndMaterialSubmission()
        {
            _submissionDepth--;
            if (_submissionDepth == 0 && _submissionPending)
            {
                _submissionPending = false;
                Reflect();
            }
        }

        public void SetColor(Color color)
        {
            EnsureBlock().SetColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveColor), color);
        }

        public void SetIntensity(float value)
        {
            EnsureBlock().SetFloat(ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveIntensity), value);
        }

        public void SetSoftEdge(float rimPower, float rimIntensity, bool isCenter)
        {
            var block = EnsureBlock();
            block.SetFloat(ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveRimPower), rimPower);
            block.SetFloat(ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveRimIntensity), rimIntensity);
            block.SetInt(ShaderManager.GetPropertyId(ShaderManager.PropertyId._IsEmissiveRimCenter), isCenter ? 1 : 0);
        }

        // Original ModelController.SetDirtParameter 0x52a5950 forwards B,G,R to
        // DirtController.SetDirtRate 0x518a600 (foot, lower, upper). That setter
        // stores the rates and tail-calls ApplyMaterial(1), which writes the
        // _DirtRate(60) Vector4 into the SAME block as colour/toon/emissive
        // (DirtController.OnInitialize 0x518a4cd copies ModelController+0xd8)
        // and flushes it to the model renderer holder. Reproduce that by putting
        // the vector into our model block and reflecting the merged state.
        // Original ModelController.SetEmissiveScrollScale 0x52a5e20 writes
        // speed into each part+0x40 and energyScale into part+0x44. The loaded
        // model sink forwards that state into the registered per-asset scroll
        // components; their material evaluation runs in model AfterLateUpdate.
        public float ScrollSpeed { get; private set; }
        public float ScrollEnergyScale { get; private set; }

        public readonly List<LiveEmissiveScrollRuntime> ScrollComponents = new List<LiveEmissiveScrollRuntime>();
        public void SetScroll(float speed, float energyScale)
        {
            ScrollSpeed = speed;
            ScrollEnergyScale = energyScale;
            foreach (var component in ScrollComponents)
            {
                component.Speed = speed;
                component.EnergyScale = energyScale;
            }
        }

        public void SetDirtRate(float foot, float lower, float upper)
        {
            Dirt.Enabled = true;
            ApplyDirtRate(foot, lower, upper);
        }

        public void SetDirtParameter(float foot, float lower, float upper, Color dirt, Color rimSpecular, Color toon)
        {
            // Original0x52a5950 does nothing without the component. Disabled
            // Dirt still receives its rates and global color state, but its
            // ApplyMaterial gate suppresses the renderer write.
            if (!HasDirtController)
                return;
            ApplyDirtRate(foot, lower, upper);
            GraphicSettings.DirtSettings?.SetColors(dirt, rimSpecular, toon);
        }

        void ApplyDirtRate(float foot, float lower, float upper)
        {
            Dirt.Rates[0] = foot;
            Dirt.Rates[1] = lower;
            Dirt.Rates[2] = upper;
            if (!Dirt.Enabled)
                return;
            Dirt.Renderers.Clear();
            Dirt.Renderers.AddRange(Renderers);
            Dirt.ApplyMaterial(false);
            int id = ShaderManager.GetPropertyId(ShaderManager.PropertyId._DirtRate);
            EnsureBlock().SetVector(id, Dirt.Block.GetVector(id));
            Reflect();
        }

        public void Reflect()
        {
            if (_submissionDepth > 0)
            {
                _submissionPending = true;
                return;
            }

            if (_block == null)
                return;
            if (_rendererBlock == null)
                _rendererBlock = new MaterialPropertyBlock();
            int color = ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveColor);
            int intensity = ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveIntensity);
            int rimPower = ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveRimPower);
            int rimIntensity = ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveRimIntensity);
            int center = ShaderManager.GetPropertyId(ShaderManager.PropertyId._IsEmissiveRimCenter);
            int dirt = ShaderManager.GetPropertyId(ShaderManager.PropertyId._DirtRate);
            bool hasColor = _block.HasProperty(color), hasIntensity = _block.HasProperty(intensity), hasRimPower = _block.HasProperty(rimPower), hasRimIntensity = _block.HasProperty(rimIntensity), hasCenter = _block.HasProperty(center), hasDirt = _block.HasProperty(dirt);
            var colorValue = hasColor ? _block.GetColor(color) : default;
            float intensityValue = hasIntensity ? _block.GetFloat(intensity) : 0f, rimPowerValue = hasRimPower ? _block.GetFloat(rimPower) : 0f, rimIntensityValue = hasRimIntensity ? _block.GetFloat(rimIntensity) : 0f;
            int centerValue = hasCenter ? _block.GetInt(center) : 0;
            var dirtValue = hasDirt ? _block.GetVector(dirt) : default;
            for (int i = 0; i < Renderers.Count; i++)
            {
                var renderer = Renderers[i];
                if (renderer == null)
                    continue;
                // Original ModelController owns one block containing BOTH
                // character lighting and emissive state. The loaded-model
                // adapter's lighting consumers currently write renderer blocks.
                // Merge our owned fields into that block: submitting _block
                // alone discards _CharaColor / toon / foot-light parameters.
                renderer.GetPropertyBlock(_rendererBlock);
                if (hasColor)
                    _rendererBlock.SetColor(color, colorValue);
                if (hasIntensity)
                    _rendererBlock.SetFloat(intensity, intensityValue);
                if (hasRimPower)
                    _rendererBlock.SetFloat(rimPower, rimPowerValue);
                if (hasRimIntensity)
                    _rendererBlock.SetFloat(rimIntensity, rimIntensityValue);
                if (hasCenter)
                    _rendererBlock.SetInt(center, centerValue);
                if (hasDirt)
                    _rendererBlock.SetVector(dirt, dirtValue);
                renderer.SetPropertyBlock(_rendererBlock);
            }
        }

        private MaterialPropertyBlock EnsureBlock()
        {
            // Unity objects must not be created in a MonoBehaviour field initializer;
            // this sink is created from initialization/update code instead.
            if (_block == null)
                _block = new MaterialPropertyBlock();
            return _block;
        }
    }

    public sealed class LiveCharacterEmissiveDispatcher
    {
        public readonly List<LiveModelEmissiveSink> Models = new List<LiveModelEmissiveSink>();
        public int Count => Models.Count;
        public float LastScrollSpeed { get; private set; }
        public float LastScrollEnergyScale { get; private set; }

        public void BeginMaterialSubmission()
        {
            for (int i = 0; i < Models.Count; i++)
                Models[i].BeginMaterialSubmission();
        }

        public void EndMaterialSubmission()
        {
            for (int i = 0; i < Models.Count; i++)
                Models[i].EndMaterialSubmission();
        }

        // Original scroll path (CharacterObject 0x4e79460 -> ModelController
        // 0x52a5e20) writes speed/energyScale into every model part
        // (part+0x40 / part+0x44). Actual loaded body/head components receive it;
        // prop-derived overrides and their registration remain separate.
        public void SetScroll(float speed, float energyScale)
        {
            LastScrollSpeed = speed;
            LastScrollEnergyScale = energyScale;
            for (int i = 0; i < Models.Count; i++)
                Models[i].SetScroll(speed, energyScale);
        }

        public LiveModelEmissiveSink AddModel()
        {
            var sink = new LiveModelEmissiveSink();
            Models.Add(sink);
            return sink;
        }

        public void SetColor(Color color)
        {
            for (int i = 0; i < Models.Count; i++)
                Models[i].SetColor(color);
            for (int i = 0; i < Models.Count; i++)
                Models[i].Reflect();
        }

        public void SetIntensity(float value)
        {
            for (int i = 0; i < Models.Count; i++)
                Models[i].SetIntensity(value);
            for (int i = 0; i < Models.Count; i++)
                Models[i].Reflect();
        }

        public void SetSoftEdge(float rimPower, float rimIntensity, bool isCenter)
        {
            for (int i = 0; i < Models.Count; i++)
                Models[i].SetSoftEdge(rimPower, rimIntensity, isCenter);
            for (int i = 0; i < Models.Count; i++)
                Models[i].Reflect();
        }

        public void SetDirtRate(float foot, float lower, float upper)
        {
            for (int i = 0; i < Models.Count; i++)
                Models[i].SetDirtRate(foot, lower, upper);
        }

        public void SetDirtParameter(float foot, float lower, float upper, Color dirt, Color rimSpecular, Color toon)
        {
            for (int i = 0; i < Models.Count; i++)
                if (Models[i] != null)
                    Models[i].SetDirtParameter(foot, lower, upper, dirt, rimSpecular, toon);
        }
    }
}
