using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // Independent Model.Component.EmissiveScroll state; never stored on the shared asset.
    public sealed class LiveEmissiveScrollRuntime
    {
        public sealed class Job
        {
            public int[] Data;
            public AnimationCurve[] Curves;
            public int[] ProcessTypes;
            public bool IsAlphaShader, IsSetShader, IsSetDirt;
            public int TextureType;
            public Shader InitialShader;
            public readonly List<Material> Materials = new List<Material>();
        }

        public readonly List<Job> Jobs = new List<Job>();
        public float TotalTime, Speed = 1f, EnergyScale = 1f, TimeScale = 1f, OtherDeltaTime;
        public bool Paused;

        static void InstallTexture(Material material, int property, Texture texture)
        {
            material.SetTexture(property, texture);

        }

        static float F(float value) => Gallop.Live.Cutt.LiveTimelineControl.MonitorFloat32(value);
        // Props has a separate initializer: clone parameters, preserve empty job
        // slots on lookup failure, and do not apply character textures/Dirt.
        public static LiveEmissiveScrollRuntime ApplyLiveProp(CharaShaderEffectData.SettingData[] settings, Material[][] materials, bool materialsReady)
        {
            LiveEmissiveScrollRuntime runtime = null;
            foreach (var setting in settings)
            {
                if (setting.Type != CharaShaderEffectData.EffectType.UVScrollEmissive)
                    continue;
                if (runtime == null)
                    runtime = new LiveEmissiveScrollRuntime();
                var job = new Job();
                runtime.Jobs.Add(job);
                if (materialsReady)
                    foreach (var group in materials)
                        foreach (var material in group)
                            if (material.name == setting.TargetMaterial.name)
                                job.Materials.Add(material);
                if (job.Materials.Count == 0)
                    continue;
                var p = (int[])setting.ParamArray.Clone();
                var first = job.Materials[0];
                job.Data = p;
                job.Curves = new AnimationCurve[3];
                job.ProcessTypes = new[]
                {
                    p[0],
                    p[2],
                    p[4]
                };
                job.IsAlphaShader = first.name.Contains("TSERA");
                job.InitialShader = first.shader;
                job.TextureType = p[9];
                job.IsSetShader = p[10] != 0;
                job.IsSetDirt = p[11] != 0;
                for (int axis = 0; axis < 3; axis++)
                    if (job.ProcessTypes[axis] == 5)
                        job.Curves[axis] = setting.ParamCurveArray[p[6 + axis]];
            }

            return runtime;
        }

        public static LiveEmissiveScrollRuntime Apply(CharaShaderEffectData.SettingData[] settings, Material[][] materials, Texture bodyDirt, Material[] bodyMaterials)
        {
            LiveEmissiveScrollRuntime runtime = null;
            foreach (var setting in settings)
            {
                if (setting.Type != CharaShaderEffectData.EffectType.UVScrollEmissive)
                    continue;
                if (runtime == null)
                    runtime = new LiveEmissiveScrollRuntime();
                // A missing RendererHolder skips initialization, as the original TryGet gate.
                if (materials == null)
                    continue;
                var job = new Job
                {
                    Data = setting.ParamArray,
                    Curves = new AnimationCurve[3]
                };
                foreach (var group in materials)
                    foreach (var material in group)
                        if (material.name == setting.TargetMaterial.name)
                            job.Materials.Add(material);
                // Original initialization reads target[0]; missing targets are a real error.
                var first = job.Materials[0];
                var p = setting.ParamArray;
                job.IsAlphaShader = first.name.Contains("TSERA");
                job.InitialShader = first.shader;
                job.ProcessTypes = new[]
                {
                    p[0],
                    p[2],
                    p[4]
                };
                job.TextureType = p[9];
                job.IsSetShader = p[10] != 0;
                job.IsSetDirt = p[11] != 0;
                for (int materialIndex = 0; materialIndex < job.Materials.Count; materialIndex++)
                {
                    if (bodyDirt != null && job.IsSetDirt)
                        InstallTexture(job.Materials[materialIndex], ShaderManager.GetPropertyId(ShaderManager.PropertyId._DirtTex), bodyDirt);
                    if (job.TextureType == 1 && bodyMaterials != null && bodyMaterials.Length > 0)
                    {
                        // RVA518c292 uses the setting/job index for ApplyTexture's target.
                        Material target = job.Materials[runtime.Jobs.Count];
                        foreach (int id in new[]
                        {
                            42,
                            44,
                            45,
                            49
                        }

                        )
                        {
                            int property = ShaderManager.GetPropertyId((ShaderManager.PropertyId)id);
                            InstallTexture(target, property, bodyMaterials[0].GetTexture(property));
                        }
                    }
                }

                for (int axis = 0; axis < 3; axis++)
                    if (job.ProcessTypes[axis] == 5)
                        job.Curves[axis] = setting.ParamCurveArray[p[6 + axis]];
                runtime.Jobs.Add(job);
            }

            return runtime;
        }

        public float ToValue(int type, int value, AnimationCurve curve, bool clamp = false)
        {
            float x = F((float)value * .001f);
            switch (type)
            {
                case 1:
                    return F(x * TotalTime);
                case 2:
                    float remainder = F(F(x * TotalTime) % 2f), difference = F(remainder - 1f);
                    return difference < 0f ? F(difference + 1f) : F(1f - difference);
                case 3:
                    x = Mathf.Sin(F(x * TotalTime));
                    break;
                case 4:
                    x = Mathf.Cos(F(x * TotalTime));
                    break;
                case 5:
                    return curve.Evaluate(F(F(x * TotalTime) % 1f));
                default:
                    return x;
            }

            return clamp ? F(F(x + 1f) * .5f) : x;
        }

        public void SetShader(int shaderType, Func<int, Shader> resolve = null)
        {
            foreach (var job in Jobs)
            {
                if (!job.IsSetShader || job.Materials.Count == 0)
                    continue;
                Shader shader;
                switch (shaderType)
                {
                    case 1:
                    case 6:
                        shader = job.InitialShader;
                        break;
                    case 2:
                    case 7:
                        int kind = job.IsAlphaShader ? 50 : 45;
                        shader = resolve == null ? ShaderManager.GetShader((ShaderManager.ShaderKinds)kind) : resolve(kind);
                        break;
                    case 3:
                    case 4:
                        shader = resolve == null ? ShaderManager.GetShader((ShaderManager.ShaderKinds)47) : resolve(47);
                        break;
                    default:
                        continue;
                }

                if (shader == null)
                    continue;
                foreach (var material in job.Materials)
                {
                    material.shader = shader;

                }
            }
        }

        public void AfterLateUpdate(bool hasOwner, float ownerDelta)
        {
            float before = TotalTime;
            if (Paused)
            {

                return;
            }

            float delta = hasOwner ? ownerDelta : OtherDeltaTime;
            TotalTime = F(TotalTime + F(F(delta * TimeScale) * Speed));
            Evaluate();

        }

        public void Evaluate()
        {
            int uv = ShaderManager.GetPropertyId(ShaderManager.PropertyId._UVEmissiveScroll), power = ShaderManager.GetPropertyId(ShaderManager.PropertyId._UVEmissivePower);
            foreach (var job in Jobs)
            {
                var p = job.Data;
                AnimationCurve Curve(int axis) => job.Curves == null ? null : job.Curves[axis];
                int Type(int axis) => job.ProcessTypes == null ? p[axis * 2] : job.ProcessTypes[axis];
                float x = ToValue(Type(0), p[1], Curve(0)), y = ToValue(Type(1), p[3], Curve(1));
                float energy = F(ToValue(Type(2), p[5], Curve(2), true) * EnergyScale);
                foreach (var material in job.Materials)
                {
                    material.SetVector(uv, new Vector4(x, y, 0, 0));
                    material.SetFloat(power, energy);
                }
            }
        }
    }
}
