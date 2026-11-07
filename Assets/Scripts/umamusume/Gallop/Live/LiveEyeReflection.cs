using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // Eye reflection context defaults decoded from the original
    // EyeReflection.Context.Default 0x5189340:
    //   +0x00 width = 64, +0x04 height = 64, +0x08 = 24, +0x0c = 1,
    //   +0x10 = EyeShader from ShaderManager.GetShader(0x41), NOT a layer mask.
    // This host context also carries the separate LiveModelController context.
    public struct LiveEyeReflectionContext
    {
        public int Width;
        public int Height;
        public int Depth;
        public Shader EyeShader;
        public bool Enabled;       // native +0x10 enable flag
        public Texture MaskTexture;// native +0x30 reflection mask texture
        public float FieldOfView;  // native +0x18
        public float NearClipPlane;// native +0x1c
        public float FarClipPlane; // native +0x20
        public int CullingMask;    // native +0x24
        public float ReflectionPower; // native +0x28

        public static LiveEyeReflectionContext Default()
        {
            return new LiveEyeReflectionContext
            {
                Width = 0x40,
                Height = 0x40,
                Depth = 0x18,
                EyeShader = ShaderManager.IsShaderBundleReady ? ShaderManager.GetShader(ShaderManager.ShaderKinds.CharacterToonEyeTRef) : null,
                Enabled = true,
                MaskTexture = null,
                FieldOfView = 60f,
                NearClipPlane = 0.03f,
                FarClipPlane = 100f,
                CullingMask = GraphicSettings.GetCullingLayer(GraphicSettings.LayerIndex.Layer3D)
                    | GraphicSettings.GetCullingLayer(GraphicSettings.LayerIndex.LayerBG)
                    | GraphicSettings.GetCullingLayer(GraphicSettings.LayerIndex.LayerEFFECT),
                ReflectionPower = 1f,
            };
        }
    }

    // Port of the original EyeReflectionController lifecycle (Gallop.Model.Component):
    //   .ctor 0x5191e40, OnInitialize 0x51918d0, OnEnable 0x5191830,
    //   OnDisable 0x5191790, OnDestroy 0x51916c0,
    //   ChangeTexture 0x5191590, ReturnTexture 0x5191c20.
    // The controller owns the reflection camera and its RenderTexture; the eye
    // EyeReflection.OnInitialize 0x5192330 switches the EYE material shader,
    // retaining its original shader for destruction, and binds _ReflectionTex.
    public sealed class LiveEyeReflectionController : MonoBehaviour
    {
        public Camera Camera;
        public GameObject CameraObject;
        public RenderTexture Texture;
        RenderTexture ownedTexture;
        public Texture ReflectionMask;
        public float ReflectionPower = 1f;
        public bool IsEnable;
        public int TextureSizeRate = 1;
        public LiveEyeReflectionContext Context;
        public readonly List<Material> TargetMaterials = new List<Material>();
        readonly Dictionary<Material, Shader> originalShaders = new Dictionary<Material, Shader>();

        static readonly int ReflectionTexId = Shader.PropertyToID("_ReflectionTex");
        static readonly int ReflectionRateId = Shader.PropertyToID("_ReflectionRate");
        static readonly int ReflectionMaskId = Shader.PropertyToID("_ReflectionMask");

        public bool Initialize(LiveEyeReflectionContext context, Transform parent = null)
        {
            Context = context;
            IsEnable = false;
            ReflectionMask = null;
            ReflectionPower = 1f;
            CreateTexture();
            CreateCamera(parent);
            if (parent != null)
            {
                TargetMaterials.Clear();
                TargetMaterials.AddRange(LiveEyeMaterialBinder.FindEyeMaterials(parent));
                foreach (var material in TargetMaterials)
                {
                    originalShaders[material] = material.shader;
                    if (context.EyeShader == null) throw new System.InvalidOperationException("Original CharacterToonEyeTRef shader is absent.");
                    material.shader = context.EyeShader;
                }
            }
            ApplyMaterialProperties();
            return true;
        }

        public RenderTexture CreateTexture()
        {
            if (Texture != null)
            {
                return Texture;
            }
            int rate = Mathf.Max(1, TextureSizeRate);
            // CreateRenderTex 0x53ee130 constructs with depth 0, then sets
            // useMipMap=false and filterMode=1 (bilinear).
            Texture = new RenderTexture(Context.Width * rate, Context.Height * rate, 0)
            {
                name = "EyeReflectionTexture",
                useMipMap = false,
                filterMode = FilterMode.Bilinear,
            };
            ownedTexture = Texture;
            return Texture;
        }

        void CreateCamera(Transform parent)
        {
            if (Camera != null)
            {
                return;
            }
            CameraObject = new GameObject("EyeReflectionCamera");
            if (parent != null)
            {
                CameraObject.transform.SetParent(parent, false);
            }
            Camera = CameraObject.AddComponent<Camera>();
            Camera.enabled = false;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.black;
            Camera.targetTexture = Texture;
            // Render before presentation cameras so the sampled texture belongs
            // to this frame. URP's main renderer also renders these base cameras.
            Camera.depth = -100f;
        }

        // UpdateEyeReflectionController 0x575e610 (model side):
        //   enabled && power>0 -> IsEnable/Power/Mask; else Power 0/IsEnable false;
        //   camera.enabled follows IsEnable; fov/near/far/cullingMask from context;
        //   then reflection power/texture are applied to the eye materials.
        public void UpdateFromContext(LiveEyeReflectionContext context)
        {
            Context = context;
            bool enabled = context.Enabled && context.ReflectionPower > 0f;
            if (enabled)
            {
                IsEnable = true;
                ReflectionPower = context.ReflectionPower;
                ReflectionMask = context.MaskTexture;
            }
            else
            {
                ReflectionPower = 0f;
                IsEnable = false;
            }
            if (Camera != null)
            {
                Camera.enabled = enabled && IsEnable;
                Camera.fieldOfView = context.FieldOfView;
                Camera.nearClipPlane = context.NearClipPlane;
                Camera.farClipPlane = context.FarClipPlane;
                Camera.cullingMask = context.CullingMask;
            }
            ApplyMaterialProperties();
        }

        public void UpdateController(bool enable, Texture mask, float power, int cullingMask,
                                     float farClip, float fieldOfView, float nearClip)
        {
            IsEnable = enable;
            ReflectionMask = mask;
            ReflectionPower = power;
            if (Camera != null)
            {
                Camera.cullingMask = cullingMask;
                Camera.farClipPlane = farClip;
                Camera.fieldOfView = fieldOfView;
                Camera.nearClipPlane = nearClip;
                Camera.enabled = enable;
            }
            ApplyMaterialProperties();
        }

        // ChangeTexture 0x5191590: bind the texture to the reflection controller
        // and the camera, then mark the material texture as changed.
        public void ChangeTexture(RenderTexture texture)
        {
            Texture = texture;
            if (Camera != null)
            {
                Camera.targetTexture = texture;
            }
            ApplyMaterialProperties();
        }

        // ReturnTexture 0x5191c20 restores the component's OWN texture, not null.
        public void ReturnTexture()
        {
            ChangeTexture(ownedTexture);
        }

        public void SetReflectionMask(Texture mask)
        {
            ReflectionMask = mask;
            ApplyMaterialProperties();
        }

        public void ApplyMaterialProperties()
        {
            foreach (var material in TargetMaterials)
            {
                if (material == null)
                {
                    continue;
                }
                if (material.HasProperty(ReflectionRateId))
                {
                    material.SetFloat(ReflectionRateId, ReflectionPower);
                }
                if (material.HasProperty(ReflectionTexId))
                {
                    material.SetTexture(ReflectionTexId, Texture);
                    // Original EyeReflection.ChangeTexture 0x5191fa0 centers a
                    // non-square reflection texture; these live targets are square.
                    if (Texture != null && material.HasProperty("_ReflectionOffsetScaleUV"))
                    {
                        float ratio = (float)System.Math.Min(Texture.width, Texture.height) / System.Math.Max(Texture.width, Texture.height);
                        material.SetVector("_ReflectionOffsetScaleUV", Texture.width <= Texture.height
                            ? new Vector4(0f, (1f-ratio)*0.5f, 1f, ratio)
                            : new Vector4((1f-ratio)*0.5f, 0f, ratio, 1f));
                    }
                }
                if (material.HasProperty(ReflectionMaskId))
                {
                    material.SetTexture(ReflectionMaskId, ReflectionMask);
                }
            }
        }

        void OnEnable()
        {
            if (Camera != null)
            {
                Camera.enabled = IsEnable;
            }
        }

        void OnDisable()
        {
            if (Camera != null)
            {
                Camera.enabled = false;
            }
            IsEnable = false;
        }

        void OnDestroy()
        {
            foreach (var pair in originalShaders)
                if (pair.Key != null) pair.Key.shader = pair.Value;
            originalShaders.Clear();
            if (CameraObject != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(CameraObject);
                }
                else
                {
                    DestroyImmediate(CameraObject);
                }
                CameraObject = null;
            }
            Camera = null;
            if (ownedTexture != null)
            {
                ownedTexture.Release();
                if (Application.isPlaying)
                {
                    Destroy(ownedTexture);
                }
                else
                {
                    DestroyImmediate(ownedTexture);
                }
                Texture = null;
                ownedTexture = null;
            }
        }
    }

    // Finds the eye materials that consume the reflection texture. The original
    // EyeReflection.SetShader/UpdateMaterial writes _ReflectionRate/_ReflectionTex/
    // _ReflectionMask through Shader.PropertyToID ids 0x5a/0x5b/0x5c.
    public static class LiveEyeMaterialBinder
    {
        public static List<Material> FindEyeMaterials(Transform root)
        {
            var result = new List<Material>();
            if (root == null)
            {
                return result;
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }
                    // Original PartsHolder eye material, not body/prop mirror
                    // materials which happen to expose _ReflectionMap.
                    if (material.shader != null && material.shader.name.StartsWith("Gallop/3D/Chara/ToonEye/", System.StringComparison.Ordinal)
                        && !result.Contains(material))
                    {
                        result.Add(material);
                    }
                }
            }
            return result;
        }
    }

    // Minimal stand-in for the original LiveModelController eye reflection link:
    //   GetEyeReflectionTransform 0x5758770: [model+0x7f8] -> [reflection+0x30]
    public sealed class LiveModelController
    {
        public object Reflection;
        public Transform ReflectionTransform;

        public Transform GetEyeReflectionTransform()
        {
            if (Reflection == null || ReflectionTransform == null)
            {
                return null;
            }
            return ReflectionTransform;
        }
    }

    public static class LiveEyeReflectionLookup
    {
        // GetEyeReflectionCameraTransform 0x4eef0e0: characterIndex from data+0x28,
        // model lookup, then GetEyeReflectionTransform.
        public static Transform GetEyeReflectionCameraTransform(int characterIndex,
                                                                System.Func<int, LiveModelController> getModel)
        {
            var model = getModel?.Invoke(characterIndex);
            if (model == null)
            {
                return null;
            }
            return model.GetEyeReflectionTransform();
        }
    }
}
