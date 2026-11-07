using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop
{
[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public class MirrorReflection : MonoBehaviour
{
    public enum Direction
    {
        Up,
        Forward,
        Right
    }

    private const string CAMERA_NAME = "Mirror Camera";
    private const string TEXTURE_NAME = "MirrorReflection Texture";
    private const float MIRROR_CAMERA_FOV_MIN = 1f;
    private const float MIRROR_CAMERA_FOV_MAX = 179f;
    private const float MIRROR_TEXTURE_SIZE_RATE_MIN = 0.01f;
    private const int MIRROR_CAMERA_DEPTH_OFFSET = -100;

    private static readonly Vector4 DEFAULT_DIST_POWER_VALUE = Vector4.zero;
    private static readonly Vector4 DEFAULT_DIST_MAP_TILE_OFFSET_VALUE = new Vector4(1f, 1f, 0f, 0f);

    [Header("镜面基础设置")]
    [SerializeField] private LayerMask _renderLayers = (1 << 8) | (1 << 12) | (1 << 13);
    [SerializeField] private int _mirrorTextureSize = 256;
    [SerializeField] private float _mirrorCliipPlaneOffset = 0f;
    [SerializeField] private Camera _baseCamera;
    [SerializeField] private float _mirrorReflectionRate = 1f;
    [SerializeField] private Vector4 _mirrorDistortionTileOffset = DEFAULT_DIST_MAP_TILE_OFFSET_VALUE;
    [SerializeField] private Vector4 _mirrorDistortionPower = new Vector4(1f, 1f, 0f, 0f);
    [SerializeField] private Direction _direction = Direction.Up;
    [SerializeField] private Color _mirrorReflectionColor = Color.white;
    [SerializeField] private Color _backgroundColor = Color.black;
    [SerializeField] private bool _useBackgroundColor = true;

    // Authored field order below follows Gallop.MirrorReflection field offsets
    // from the original assembly. Unity's MonoScript properties hash depends
    // on this layout; changing it makes the serialized prefab component appear
    // as a missing script and forces the auto-attach fallback.
    [Header("材质贴图大小")]
    [SerializeField] private bool _useMirrorTextureScale = false;
    [SerializeField] private float _mirrorTextureScaleForBaseCamera = 1f;

    [Header("Runtime State")]
    private Transform _baseCameraTransform;
    private Transform _transform;
    private Camera _mirrorCamera;
    private Gallop.RenderPipeline.CameraData _mirrorCameraData;
    private bool _isEnabledMirrorCamera = true;
    private Transform _mirrorCameraTransform;
    private RenderTexture _mirrorTexture;
    private int _oldMirrorTextureSize = -1;
    private Renderer _receivedMirrorMeshRenderer;
    private LayerMask _finalRenderLayers = ~0;
    private int _objectLayer;
    private bool _isInitialized;
    private Material[] _materials;
    private bool _isEnabled = true;
    private int _mirrorIndex = -1;
    private bool _isUseBaseCameraTextureSize = false;
    private Vector4 _clipPlane;
    private int _beginRenderCRPId;
    private Vector4 _initMirrorDistortionPower = DEFAULT_DIST_POWER_VALUE;
    private Vector4 _initMirrorDistortionTileOffset = DEFAULT_DIST_MAP_TILE_OFFSET_VALUE;
    private float _oldMirrorTextureScaleForBaseCamera = -1f;

    // Not serialized by native MirrorReflection; kept as host controls only.
    private bool _autoInitialize = true;
    private bool _autoFindBaseCamera = true;
    private bool _logDebug = false;
    private bool _legacyBuiltInFallback = false;

    private Func<float> _getBaseCameraFovFactor;
    private Action<MirrorReflection> OnPreDraw;
    private Action<MirrorReflection> OnPostDraw;
    private event Action<ScriptableRenderContext, Camera> _beginCameraRenderingCallbacks;
    private event Action<ScriptableRenderContext, Camera> _endCameraRenderingCallbacks;

    private bool _isRenderingNow;
    private readonly Stack<int> _mirrorObjectLayers = new Stack<int>(4);
    internal Live.StageController LifecycleOwner { get; set; }

    private readonly UniversalRenderPipeline.SingleCameraRequest _singleCameraRequest =
        new UniversalRenderPipeline.SingleCameraRequest();

    private static readonly int PID_Color = Shader.PropertyToID("_Color");
    private static readonly int PID_ReflectionRate = Shader.PropertyToID("_ReflectionRate");
    private static readonly int PID_ReflectionTex = Shader.PropertyToID("_ReflectionTex");
    private static readonly int PID_DistMapST = Shader.PropertyToID("_DistMap_ST");
    private static readonly int PID_DistPower = Shader.PropertyToID("_DistPower");

    public Camera BaseCamera => _baseCamera;
    public Camera MirrorCamera => _mirrorCamera;
    public RenderTexture MirrorTexture => _mirrorTexture;
    public bool IsInitialized => _isInitialized;
    public bool IsUseBaseCameraTextureSize => _isUseBaseCameraTextureSize;
    public int MirrorIndex => _mirrorIndex;

    public float MirrorClipPlaneOffset
    {
        get => _mirrorCliipPlaneOffset;
        set => _mirrorCliipPlaneOffset = value;
    }

    public float MirrorReflectionRate
    {
        get => _mirrorReflectionRate;
        set
        {
            _mirrorReflectionRate = value;

            if (_baseCamera != null && _mirrorCamera != null && _isEnabledMirrorCamera)
                UpdateMirrorParams();
            else
                ApplyMaterialParams();
        }
    }

    public Vector4 MirrorDistortionTileOffset
    {
        get => _mirrorDistortionTileOffset;
        set
        {
            _mirrorDistortionTileOffset = value;
            ApplyMaterialParams();
        }
    }

    public Vector4 MirrorDistortionPower
    {
        get => _mirrorDistortionPower;
        set
        {
            _mirrorDistortionPower = value;
            ApplyMaterialParams();
        }
    }

    public Direction Dir
    {
        get => _direction;
        set => _direction = value;
    }

    public Color MirrorReflectionColor
    {
        get => _mirrorReflectionColor;
        set
        {
            _mirrorReflectionColor = value;
            ApplyMaterialParams();
        }
    }

    public Vector4 GetDefaultMirrorDistortionPower() => _initMirrorDistortionPower;
    public Vector4 GetDefaultMirrorDistortionMapTileOffset() => _initMirrorDistortionTileOffset;

    public void Reset()
    {
        _transform = transform;
        _receivedMirrorMeshRenderer = GetComponent<Renderer>();
        _objectLayer = gameObject.layer;
        _finalRenderLayers = _renderLayers;
    }

    private void Awake()
    {
        EnsureSelfReferences();



        if (_autoInitialize)
            TryAutoInitializeIfPossible();
    }

    private void OnEnable()
    {
        _isEnabled = true;
        RenderPipelineManager.beginCameraRendering += OnBeginMirrorCamera;
        RenderPipelineManager.endCameraRendering += OnEndMirrorCamera;

        if (_autoInitialize && !_isInitialized)
        {
            TryAutoInitializeIfPossible();
            return;
        }

        if (_isInitialized)
        {
            UpdateRenderTexture();
            UpdateMirrorTexture();
            UpdateMirrorParams();
        }
    }

    private void OnDisable()
    {
        _isEnabled = false;
        RenderPipelineManager.beginCameraRendering -= OnBeginMirrorCamera;
        RenderPipelineManager.endCameraRendering -= OnEndMirrorCamera;
        if (_mirrorCamera != null) _mirrorCamera.enabled = false;
        LifecycleOwner?.RestoreMirrorActorLayers(_mirrorCamera);
        RestoreMirrorObjectLayers();
    }

    private void LateUpdate()
    {
        if (!_isEnabled || !_isEnabledMirrorCamera)
            return;

        if (!_isInitialized)
        {
            if (_autoInitialize)
                TryAutoInitializeIfPossible();

            if (!_isInitialized)
                return;
        }

        if (_isRenderingNow)
            return;

        if (_baseCamera == null || _mirrorCamera == null || _baseCameraTransform == null)
            return;

        PrepareAutomaticRender();
    }

    // Native MirrorReflection.LateUpdate keeps the mirror Camera enabled and
    // lets the render pipeline render it as a normal camera. The original
    // implementation updates the RT/projection in OnBeginRender; doing it here
    // is the managed equivalent for the URP camera loop.
    public void PrepareAutomaticRender()
    {
        if (!isActiveAndEnabled || !_isEnabled || !_isEnabledMirrorCamera)
        {
            if (_mirrorCamera != null) _mirrorCamera.enabled = false;
            return;
        }
        if (!_isInitialized)
        {
            if (_autoInitialize) TryAutoInitializeIfPossible();
            if (!_isInitialized) { if (_mirrorCamera != null) _mirrorCamera.enabled = false; return; }
        }
        if (_isRenderingNow) return;
        if (_baseCamera == null || _mirrorCamera == null || _baseCameraTransform == null)
        {
            _mirrorCamera.enabled = false;
            return;
        }
        if (_receivedMirrorMeshRenderer == null || !_receivedMirrorMeshRenderer.enabled)
        {
            _mirrorCamera.enabled = false;
            return;
        }

        UpdateRenderTexture();
        UpdateMirrorParams();
        if (!IsBaseCameraInFrontOfMirror())
        {
            _mirrorCamera.enabled = false;
            return;
        }
        UpdateProjectionMatrix();
        _mirrorCamera.enabled = true;
    }

    private void OnBeginMirrorCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != _mirrorCamera || _mirrorCamera == null) return;
        if (!_isEnabled || !_isEnabledMirrorCamera) return;
        if (!_isInitialized) return;

        // Native OnBeginRender refreshes the mirror projection immediately
        // before the camera's draw, after the presentation camera has settled.
        _isRenderingNow = true;
        UpdateRenderTexture();
        UpdateMirrorParams();
        UpdateProjectionMatrix();
        _beginCameraRenderingCallbacks?.Invoke(context, _mirrorCamera);
    }

    private void OnEndMirrorCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != _mirrorCamera || _mirrorCamera == null) return;
        if (!_isRenderingNow) return;
        _endCameraRenderingCallbacks?.Invoke(context, _mirrorCamera);
        _isRenderingNow = false;
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginMirrorCamera;
        RenderPipelineManager.endCameraRendering -= OnEndMirrorCamera;
        if (_mirrorCamera != null) _mirrorCamera.enabled = false;
        LifecycleOwner?.ReleaseMirrorReflection(this);
        RestoreMirrorObjectLayers();
        ReleaseMirrorTexture();

        if (_mirrorCamera != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(_mirrorCamera.gameObject);
            else Destroy(_mirrorCamera.gameObject);
#else
            Destroy(_mirrorCamera.gameObject);
#endif
        }
    }

    private void EnsureSelfReferences()
    {
        if (_transform == null)
            _transform = transform;

        if (_receivedMirrorMeshRenderer == null)
            _receivedMirrorMeshRenderer = GetComponent<Renderer>();

        _objectLayer = gameObject.layer;
        _finalRenderLayers = _renderLayers;
    }

    private bool IsUsableCamera(Camera cam)
    {
        if (cam == null)
            return false;

        if (_mirrorCamera != null && cam == _mirrorCamera)
            return false;

        if (!cam.enabled)
            return false;

        if (!cam.gameObject.activeInHierarchy)
            return false;

        return true;
    }

    private bool EnsureBaseCamera()
    {
        if (_baseCamera != null)
        {
            if (IsUsableCamera(_baseCamera))
            {
                _baseCameraTransform = _baseCamera.transform;
                return true;
            }

            _baseCamera = null;
            _baseCameraTransform = null;
        }

        if (!_autoFindBaseCamera)
            return false;

        if (IsUsableCamera(Camera.main))
        {
            _baseCamera = Camera.main;
            _baseCameraTransform = _baseCamera.transform;
            return true;
        }

        Camera[] all = Camera.allCameras;
        for (int i = 0; i < all.Length; i++)
        {
            if (!IsUsableCamera(all[i]))
                continue;

            _baseCamera = all[i];
            _baseCameraTransform = _baseCamera.transform;
            return true;
        }

        return false;
    }

    private void TryAutoInitializeIfPossible()
    {
        // During live playback StageController is the sole initialization owner.
        if (Live.Director.instance != null || GetComponentInParent<Live.StageController>() != null)
            return;
        EnsureSelfReferences();

        if (!EnsureBaseCamera())
            return;

        Initialize(_baseCamera, _mirrorIndex, _isUseBaseCameraTextureSize);
    }

    // Restores the authored prefab fields recovered from the original stage
    // bundle. Called by StageController before Initialize.
    internal void ApplyPrefabRow(MirrorReflectionPrefabTable.Row row)
    {
        if (row == null) return;
        _mirrorTextureSize = row.TextureSize;
        _mirrorCliipPlaneOffset = row.Clip;
        _mirrorReflectionRate = row.Rate;
        _mirrorDistortionTileOffset = new Vector4(row.DistTile[0], row.DistTile[1], row.DistTile[2], row.DistTile[3]);
        _mirrorDistortionPower = new Vector4(row.DistPower[0], row.DistPower[1], row.DistPower[2], row.DistPower[3]);
        _direction = (Direction)row.Direction;
        _mirrorReflectionColor = new Color(row.ReflectionColor[0], row.ReflectionColor[1], row.ReflectionColor[2], row.ReflectionColor[3]);
        _backgroundColor = new Color(row.Background[0], row.Background[1], row.Background[2], row.Background[3]);
        _useBackgroundColor = row.UseBackground;
        _useMirrorTextureScale = row.UseScale;
        _mirrorTextureScaleForBaseCamera = row.Scale;
        _renderLayers = row.Layers;
    }

    public void Initialize(Camera baseCamera, int mirrorIndex = -1, bool isUseBaseCameraTextureSize = false)
    {
        LifecycleOwner?.PrepareMirrorReinitialization(this);
        RestoreMirrorObjectLayers();
        _isInitialized = false;

        EnsureSelfReferences();

        if (baseCamera != null)
        {
            _baseCamera = baseCamera;
            _baseCameraTransform = baseCamera.transform;
        }

        if (!EnsureBaseCamera())
        {
            Log("Initialize failed: base camera not found.");
            return;
        }

        if (_receivedMirrorMeshRenderer == null)
        {
            Log("Initialize failed: renderer missing.");
            return;
        }

        Material sharedMaterial = _receivedMirrorMeshRenderer.sharedMaterial;
        if (sharedMaterial == null &&
            (_receivedMirrorMeshRenderer.sharedMaterials == null || _receivedMirrorMeshRenderer.sharedMaterials.Length == 0))
        {
            Log("Initialize failed: no shared material.");
            return;
        }

        _mirrorIndex = mirrorIndex;
        _isUseBaseCameraTextureSize = isUseBaseCameraTextureSize;
        _transform = transform;
        _objectLayer = gameObject.layer;

        InitMaterials();
        CreateMirrorCamera();
        UpdateRenderTexture();
        UpdateMirrorTexture();
        UpdateMirrorParams();

        _isInitialized = true;
        LifecycleOwner?.CompleteMirrorReinitialization(this);
        Log($"Initialize success. mirrorIndex={_mirrorIndex}, useBaseCameraTextureSize={_isUseBaseCameraTextureSize}");
    }

    public void SetupBaseCamera(Camera baseCamera, Func<float> getFovFactorFunc)
    {
        if (baseCamera != null)
        {
            _baseCamera = baseCamera;
            _baseCameraTransform = baseCamera.transform;
        }
        else
        {
            EnsureBaseCamera();
        }

        _getBaseCameraFovFactor = getFovFactorFunc;
        UpdateMirrorParams();
    }

    public void ResetCullingMask()
    {
        _finalRenderLayers = _renderLayers;
        if (_mirrorCamera != null)
            _mirrorCamera.cullingMask = _finalRenderLayers;
    }

    public void AddCullingMask(int cullingMask)
    {
        _finalRenderLayers |= cullingMask;
        if (_mirrorCamera != null)
            _mirrorCamera.cullingMask = _finalRenderLayers;
    }

    public void RemoveCullingMask(int cullingMask)
    {
        _finalRenderLayers &= ~cullingMask;
        if (_mirrorCamera != null)
            _mirrorCamera.cullingMask = _finalRenderLayers;
    }

    public void AddDrawCallback(Action<MirrorReflection> preDraw, Action<MirrorReflection> postDraw)
    {
        OnPreDraw += preDraw;
        OnPostDraw += postDraw;
    }

    public void RemoveDrawCallback(Action<MirrorReflection> preDraw, Action<MirrorReflection> postDraw)
    {
        OnPreDraw -= preDraw;
        OnPostDraw -= postDraw;
    }

    public void AddBeginCameraRenderingCallback(Action<ScriptableRenderContext, Camera> callback)
    {
        _beginCameraRenderingCallbacks += callback;
    }

    public void RemoveBeginCameraRenderingCallback(Action<ScriptableRenderContext, Camera> callback)
    {
        _beginCameraRenderingCallbacks -= callback;
    }

    public void AddEndCameraRenderingCallback(Action<ScriptableRenderContext, Camera> callback)
    {
        _endCameraRenderingCallbacks += callback;
    }

    public void RemoveEndCameraRenderingCallback(Action<ScriptableRenderContext, Camera> callback)
    {
        _endCameraRenderingCallbacks -= callback;
    }

    public void SetMirrorObjectLayer(int layer)
    {
        if (!_isInitialized || layer < 0)
            return;
        _objectLayer = gameObject.layer;
        _mirrorObjectLayers.Push(_objectLayer);
        SetMirrorObjectLayerRecursively(transform, layer);
    }

    public void ResetMirrorObjectLayer()
    {
        if (_mirrorObjectLayers.Count == 0)
            return;
        _objectLayer = _mirrorObjectLayers.Pop();
        SetMirrorObjectLayerRecursively(transform, _objectLayer);
    }

    internal void RestoreMirrorObjectLayers()
    {
        while (_mirrorObjectLayers.Count > 0)
            ResetMirrorObjectLayer();
    }

    private static void SetMirrorObjectLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetMirrorObjectLayerRecursively(root.GetChild(i), layer);
    }

    public void SetActive(bool active)
    {
        // Native SetActive (0x19b7e10) gates on initialization and hides the receiver too.
        if (_isInitialized && gameObject.activeSelf != active)
            gameObject.SetActive(active);
    }

    public void SetLightMirrorShader(bool isEnable)
    {
        // Native RVA 0x19b7f90 writes CameraData.MirrorCasterQuality (0/1).
        // GallopRenderer replaces both forward queues with SimpleMirrorCaster
        // when quality is 1; renderer slot 3 is the independent Gallop mirror renderer.
        if (_mirrorCamera != null)
        {
            var owner = _mirrorCameraData != null
                ? _mirrorCameraData
                : _mirrorCamera.GetComponent<Gallop.RenderPipeline.CameraData>();
            if (owner != null) owner.MirrorCasterQuality = isEnable ? 1 : 0;
            // Native SetLightMirrorShader 0x3faa590 writes only
            // CameraData.MirrorCasterQuality (+0xdc). It does not select a
            // renderer slot here; GallopRenderer consumes the quality flag.
        }
    }

    public void SetMirrorVisibleOfficialStyle(bool visible, bool pauseRenderWhenHidden = true)
    {
        if (visible)
        {
            _isEnabled = true;
            enabled = true;
            _isEnabledMirrorCamera = true;

            if (_isInitialized)
            {
                UpdateRenderTexture();
                UpdateMirrorTexture();
                UpdateMirrorParams();
            }
            else if (_autoInitialize)
            {
                TryAutoInitializeIfPossible();
            }

            return;
        }

        // �ر�ʱֻ�ѵ�ǰ���ʷ���ǿ��ѹ�� 0�����ⶳ�����һ֡������ʾ
        _mirrorReflectionRate = 0f;
        ApplyMaterialParams();

        if (pauseRenderWhenHidden)
        {
            _isEnabledMirrorCamera = false;
            if (_mirrorCamera != null) _mirrorCamera.enabled = false;
        }
    }

    public void RemakeMirrorTexture()
    {
        ReleaseMirrorTexture();
        UpdateRenderTexture();
        UpdateMirrorTexture();
        UpdateMirrorParams();
    }

    private void InitMaterials()
    {
        if (_receivedMirrorMeshRenderer == null)
            return;

        _materials = _receivedMirrorMeshRenderer.materials;
        if (_materials == null || _materials.Length == 0)
            return;

        bool foundDistPower = false;
        bool foundDistMapST = false;

        _initMirrorDistortionPower = DEFAULT_DIST_POWER_VALUE;
        _initMirrorDistortionTileOffset = DEFAULT_DIST_MAP_TILE_OFFSET_VALUE;

        for (int i = 0; i < _materials.Length; i++)
        {
            Material mat = _materials[i];
            if (mat == null)
                continue;

            if (!foundDistPower && mat.HasProperty(PID_DistPower))
            {
                _initMirrorDistortionPower = mat.GetVector(PID_DistPower);
                foundDistPower = true;
            }

            if (!foundDistMapST && mat.HasProperty(PID_DistMapST))
            {
                _initMirrorDistortionTileOffset = mat.GetVector(PID_DistMapST);
                foundDistMapST = true;
            }

            if (foundDistPower && foundDistMapST)
                break;
        }

        _mirrorDistortionPower = _initMirrorDistortionPower;
        _mirrorDistortionTileOffset = _initMirrorDistortionTileOffset;

        Log($"InitMaterials done. foundDistPower={foundDistPower}, foundDistMapST={foundDistMapST}");
    }

    private void CreateMirrorCamera()
    {
        if (_mirrorCamera != null)
        {
            _mirrorCameraTransform = _mirrorCamera.transform;
            return;
        }

        GameObject go = new GameObject(CAMERA_NAME, typeof(Camera));
        go.hideFlags = HideFlags.DontSave;

        _mirrorCamera = go.GetComponent<Camera>();
        // Original CreateMirrorCamera 0x4a8acd0 explicitly adds FlareLayer.
        Gallop.RenderPipeline.NativeLensFlareCameraRegistry.RestoreSerializedFlareLayer(_mirrorCamera);
        // Installed MirrorReflection.CreateMirrorCamera 0x3ee2950 sets CameraData+d9.
        // This selects the native flare event549 before mirror post-processing.
        _mirrorCameraData = go.AddComponent<Gallop.RenderPipeline.CameraData>();
        _mirrorCameraData.IsMirrorRendering = true;
        // Current native CreateMirrorCamera 0x3fa8bf1 initializes quality to 1.
        // A timeline's IsToonMirror can subsequently select the full caster.
        _mirrorCameraData.MirrorCasterQuality = 1;
        // Native OnBeginRender re-culls with the reflected camera and publishes
        // its matrices before drawing. URP14 culls before render passes, so the
        // begin-camera callback prepares culling and GallopBeforePass publishes
        // the same matrices after SetupCameraProperties.
        _mirrorCameraData.ForceViewProjectionMatrix = true;
        // Native CreateMirrorCamera explicitly enables the mirror blur:
        // ImageEffectParameter.BlurOptimized (+0x150/+0x24):
        // IsEnable=1, Downsample=0, BlurSize=1.0, BlurIterations=1.
        var blur = _mirrorCameraData.Parameter.BlurOptimize;
        blur.IsEnable = true;
        blur.Downsample = 0;
        blur.BlurSize = 1f;
        blur.BlurIterations = 1;
        _mirrorCameraData.Parameter.BlurOptimize = blur;
        // A runtime-created Camera needs URP's additional camera data before
        // the camera loop will submit it. Native CreateMirrorCamera creates the
        // same camera data stack; without this the enabled mirror camera never
        // reaches beginCameraRendering and its RenderTexture stays unwritten.
        var urpCamera = go.AddComponent<UniversalAdditionalCameraData>();
        urpCamera.renderType = CameraRenderType.Base;
        urpCamera.renderPostProcessing = false;
        urpCamera.renderShadows = false;
        // Native CreateMirrorCamera does not select a renderer slot. It keeps
        // the pipeline default (GallopRenderer, slot 2), because that renderer
        // owns "Gallop Live Post Effects" and therefore the mirror blur pass.
        // GallopSimpleMirrorRenderer (slot 3) has no PostImageEffectFeature.
        _mirrorCameraTransform = go.transform;

        _mirrorCameraTransform.SetParent(_transform, true);
        _mirrorCameraTransform.position = _transform.position;
        _mirrorCameraTransform.rotation = _transform.rotation;
        _mirrorCameraTransform.localScale = Vector3.one;

        _mirrorCamera.allowHDR = false;
        _mirrorCamera.allowMSAA = false;
        _mirrorCamera.enabled = false;
        _mirrorCamera.depth = MIRROR_CAMERA_DEPTH_OFFSET;
        _mirrorCamera.clearFlags = CameraClearFlags.Color;
        _mirrorCamera.cullingMask = _finalRenderLayers;
        _mirrorCamera.stereoTargetEye = StereoTargetEyeMask.None;

        Log("CreateMirrorCamera done.");
    }

    private Vector2Int GetRenderTextureSize()
    {
        // Host equivalent of GraphicSettings.GetVirtualResolution3D: the Live
        // presentation surface, independent of a selected monitor/base camera.
        var presentation = Live.Director.instance != null ? Live.Director.instance.MainRenderCamera : _baseCamera;
        var surface = presentation != null ? presentation.targetTexture : null;
        var virtualSize = surface != null ? new Vector2Int(surface.width, surface.height)
            : presentation != null ? new Vector2Int(presentation.pixelWidth, presentation.pixelHeight)
            : new Vector2Int(Screen.width, Screen.height);
        var baseTexture = _baseCamera != null ? _baseCamera.targetTexture : null;
        return CalculateRenderTextureSize(virtualSize, _mirrorTextureSize, _useMirrorTextureScale,
            _mirrorCamera != null && _mirrorCamera.allowMSAA, _isUseBaseCameraTextureSize,
            baseTexture != null ? new Vector2Int(baseTexture.width, baseTexture.height) : Vector2Int.zero,
            _mirrorTextureScaleForBaseCamera);
    }

    // GetRenderTextureSize 0x3fa8d90. Fixed size is a 16:9 reference width;
    // scale mode uses thousandths, and every branch retains the final +1 row.
    internal static Vector2Int CalculateRenderTextureSize(Vector2Int virtualSize, int size,
        bool scaleMode, bool msaa, bool useBaseTexture, Vector2Int baseSize, float baseScale)
    {
        if (msaa) { scaleMode = true; size = 1000; baseScale = 1f; }
        int width, height;
        if (useBaseTexture && baseSize.x > 0 && baseSize.y > 0)
        {
            // Native falls back to full size below the minimum accepted rate.
            float scale = baseScale < MIRROR_TEXTURE_SIZE_RATE_MIN ? 1f : baseScale;
            width = Mathf.FloorToInt(baseSize.x * scale);
            height = Mathf.FloorToInt(baseSize.y * scale);
        }
        else if (scaleMode)
        {
            width = Mathf.FloorToInt((float)unchecked(virtualSize.x * size) / 1000f);
            height = Mathf.FloorToInt((float)unchecked(virtualSize.y * size) / 1000f);
        }
        else
        {
            height = Mathf.FloorToInt((float)size * 720f / 1280f);
            width = Mathf.FloorToInt((float)unchecked(height * virtualSize.x) / virtualSize.y);
        }
        return new Vector2Int(width, height + 1);
    }

    private void UpdateRenderTexture()
    {
        Vector2Int size = GetRenderTextureSize();
        bool needRebuild =
            _mirrorTexture == null ||
            _oldMirrorTextureSize != _mirrorTextureSize ||
            !Mathf.Approximately(_oldMirrorTextureScaleForBaseCamera, _mirrorTextureScaleForBaseCamera) ||
            _mirrorTexture.width != size.x || _mirrorTexture.height != size.y;

        if (!needRebuild)
            return;

        ReleaseMirrorTexture();

        _oldMirrorTextureScaleForBaseCamera = _mirrorTextureScaleForBaseCamera;

        _mirrorTexture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32)
        {
            name = TEXTURE_NAME,
            hideFlags = HideFlags.DontSave,
            useMipMap = false,
            autoGenerateMips = false,
        };

        _mirrorTexture.Create();

        if (_mirrorCamera != null)
            _mirrorCamera.targetTexture = _mirrorTexture;

        UpdateMirrorTexture();

        _oldMirrorTextureSize = _mirrorTextureSize;

        Log($"UpdateRenderTexture done. {size.x}x{size.y}");
    }

    private void SetMirrorTextureOnMaterials(Texture tex)
    {
        if (_materials == null)
            return;

        for (int i = 0; i < _materials.Length; i++)
        {
            Material mat = _materials[i];
            if (mat == null)
                continue;

            if (mat.HasProperty(PID_ReflectionTex))
                mat.SetTexture(PID_ReflectionTex, tex);
        }
    }

    private void UpdateMirrorTexture()
    {
        SetMirrorTextureOnMaterials(_mirrorTexture);
    }

    private void ReleaseMirrorTexture()
    {
        SetMirrorTextureOnMaterials(null);

        if (_mirrorTexture == null)
            return;

        if (_mirrorCamera != null && _mirrorCamera.targetTexture == _mirrorTexture)
            _mirrorCamera.targetTexture = null;

        _mirrorTexture.Release();

#if UNITY_EDITOR
    if (!Application.isPlaying) DestroyImmediate(_mirrorTexture);
    else Destroy(_mirrorTexture);
#else
        Destroy(_mirrorTexture);
#endif

        _mirrorTexture = null;
    }

    public void UpdateMirrorParams()
    {
        if (_baseCamera == null || !_isEnabledMirrorCamera || _mirrorCamera == null)
            return;

        _mirrorCamera.clearFlags = CameraClearFlags.Color;
        _mirrorCamera.backgroundColor = _useBackgroundColor ? _backgroundColor : _baseCamera.backgroundColor;

        float fov = _baseCamera.fieldOfView;
        if (_getBaseCameraFovFactor != null)
        {
            try
            {
                fov *= _getBaseCameraFovFactor();
            }
            catch
            {
            }
        }

        _mirrorCamera.fieldOfView = Mathf.Clamp(fov, MIRROR_CAMERA_FOV_MIN, MIRROR_CAMERA_FOV_MAX);
        _mirrorCamera.nearClipPlane = _baseCamera.nearClipPlane;
        _mirrorCamera.farClipPlane = _baseCamera.farClipPlane;
        _mirrorCamera.orthographic = _baseCamera.orthographic;
        _mirrorCamera.orthographicSize = _baseCamera.orthographicSize;
        _mirrorCamera.targetTexture = _mirrorTexture;
        _mirrorCamera.cullingMask = _finalRenderLayers;

        if (_mirrorTexture != null && _mirrorTexture.height > 0)
            _mirrorCamera.aspect = (float)_mirrorTexture.width / _mirrorTexture.height;
        else
            _mirrorCamera.aspect = _baseCamera.aspect;

        ApplyMaterialParams();
    }

    private void ApplyMaterialParams()
    {
        if (_materials == null)
            return;

        for (int i = 0; i < _materials.Length; i++)
        {
            Material mat = _materials[i];
            if (mat == null)
                continue;

            if (mat.HasProperty(PID_ReflectionRate))
                mat.SetFloat(PID_ReflectionRate, _mirrorReflectionRate);

            if (mat.HasProperty(PID_Color))
                mat.SetColor(PID_Color, _mirrorReflectionColor);

            if (mat.HasProperty(PID_DistMapST))
                mat.SetVector(PID_DistMapST, _mirrorDistortionTileOffset);

            if (mat.HasProperty(PID_DistPower))
                mat.SetVector(PID_DistPower, _mirrorDistortionPower);
        }
    }

    private Vector3 GetMirrorNormal()
    {
        switch (_direction)
        {
            case Direction.Forward:
                return _transform.forward;
            case Direction.Right:
                return _transform.right;
            case Direction.Up:
            default:
                return _transform.up;
        }
    }

    private bool IsPointAbovePlane(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
    {
        return Vector3.Dot(planeNormal, (point - planePoint).normalized) > 0f;
    }

    private bool IsBaseCameraInFrontOfMirror()
    {
        if (_baseCameraTransform == null || _transform == null)
            return false;

        return IsPointAbovePlane(_baseCameraTransform.position, _transform.position, GetMirrorNormal());
    }

    private void UpdateProjectionMatrix()
    {
        if (_baseCamera == null || _baseCameraTransform == null || _mirrorCamera == null || _mirrorCameraTransform == null)
            return;

        Vector3 normal = GetMirrorNormal();
        Vector3 planePos = _transform.position;

        float d = -Vector3.Dot(normal, planePos) - _mirrorCliipPlaneOffset;
        Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

        Matrix4x4 reflectionMat = Matrix4x4.zero;
        CalculateReflectionMatrix(ref reflectionMat, reflectionPlane.x, reflectionPlane.y, reflectionPlane.z, reflectionPlane.w);

        Vector3 reflectedBasePos = reflectionMat.MultiplyPoint(_baseCameraTransform.position);

        _mirrorCamera.worldToCameraMatrix = _baseCamera.worldToCameraMatrix * reflectionMat;

        Vector3 offsetPlanePos = planePos + normal * _mirrorCliipPlaneOffset;
        Matrix4x4 mirrorWorldToCamera = _mirrorCamera.worldToCameraMatrix;
        Vector3 camSpacePoint = mirrorWorldToCamera.MultiplyPoint(offsetPlanePos);
        Vector3 camSpaceNormal = mirrorWorldToCamera.MultiplyVector(normal).normalized;
        _clipPlane = new Vector4(camSpaceNormal.x,camSpaceNormal.y,camSpaceNormal.z,-Vector3.Dot(camSpacePoint, camSpaceNormal));

        float oldBaseFov = _baseCamera.fieldOfView;

        try
        {
            _baseCamera.fieldOfView = _mirrorCamera.fieldOfView;
            _baseCamera.aspect = _mirrorCamera.aspect;

            Matrix4x4 projection = _baseCamera.projectionMatrix;
            CalculateObliqueMatrix(ref projection, _clipPlane);
            _mirrorCamera.projectionMatrix = projection;
            // Original 0x3ee539e..0x3ee53d5 culls using the final oblique
            // projection, not the base camera's unclipped culling matrix.
            _mirrorCamera.cullingMatrix = projection * mirrorWorldToCamera;
        }
        finally
        {
            _baseCamera.fieldOfView = oldBaseFov;
            _baseCamera.ResetAspect();
        }

        _mirrorCameraTransform.position = reflectedBasePos;
        // Native 0x3fab3a1 sets only Transform pitch to zero. The reflected view
        // and oblique projection above remain authoritative for rendering.
        Vector3 baseEuler = _baseCameraTransform.eulerAngles;
        _mirrorCameraTransform.eulerAngles = new Vector3(0f, baseEuler.y, baseEuler.z);
    }

    private static float Sgn(float x)
    {
        if (x > 0f) return 1f;
        if (x < 0f) return -1f;
        return 0f;
    }
    // 根据裁剪平面修改投影矩阵,生成斜截投影矩阵
    private static void CalculateObliqueMatrix(ref Matrix4x4 projection, Vector4 clipPlane)
    {
        Vector4 q = projection.inverse * new Vector4(
            Sgn(clipPlane.x),
            Sgn(clipPlane.y),
            1f,
            1f
        );

        Vector4 c = clipPlane * (2f / Vector4.Dot(clipPlane, q));

        projection[2] = c.x - projection[3];
        projection[6] = c.y - projection[7];
        projection[10] = c.z - projection[11];
        projection[14] = c.w - projection[15];
    }
    //反射矩阵计算
    private static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, float x, float y, float z, float w)
    {
        reflectionMat.m00 = 1f - 2f * x * x;
        reflectionMat.m01 = -2f * x * y;
        reflectionMat.m02 = -2f * x * z;
        reflectionMat.m03 = -2f * x * w;

        reflectionMat.m10 = -2f * y * x;
        reflectionMat.m11 = 1f - 2f * y * y;
        reflectionMat.m12 = -2f * y * z;
        reflectionMat.m13 = -2f * y * w;

        reflectionMat.m20 = -2f * z * x;
        reflectionMat.m21 = -2f * z * y;
        reflectionMat.m22 = 1f - 2f * z * z;
        reflectionMat.m23 = -2f * z * w;

        reflectionMat.m30 = 0f;
        reflectionMat.m31 = 0f;
        reflectionMat.m32 = 0f;
        reflectionMat.m33 = 1f;
    }

    private bool TrySubmitSingleCameraRequest()
    {
        if (_mirrorCamera == null || _mirrorTexture == null)
            return false;

        _singleCameraRequest.destination = _mirrorTexture;

        if (!UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(_mirrorCamera, _singleCameraRequest))
        {
            Log("SingleCameraRequest is not supported by the active render pipeline.");
            return false;
        }

        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(_mirrorCamera, _singleCameraRequest);
        return true;
    }

    public void ForceRenderOnce()
    {
        if (!isActiveAndEnabled || !_isEnabled || !_isEnabledMirrorCamera) return;
        if (!_isInitialized)
            TryAutoInitializeIfPossible();

        if (!_isInitialized || _baseCamera == null || _mirrorCamera == null ||
            _receivedMirrorMeshRenderer == null || !_receivedMirrorMeshRenderer.enabled)
            return;

        if (_isRenderingNow)
            return;

        _isRenderingNow = true;
        bool oldInvert = GL.invertCulling;
        int receiverLayerDepth = _mirrorObjectLayers.Count;
        try
        {
            UpdateRenderTexture();
            UpdateMirrorParams();

            if (!IsBaseCameraInFrontOfMirror())
                return;

            UpdateProjectionMatrix();

            OnPreDraw?.Invoke(this);
            _beginCameraRenderingCallbacks?.Invoke(default, _mirrorCamera);

            // GallopRenderer enqueues the native invert pair itself.
            bool gallopOwnsCulling = _mirrorCamera.GetUniversalAdditionalCameraData().scriptableRenderer is Gallop.RenderPipeline.GallopRenderer;
            if (!gallopOwnsCulling) GL.invertCulling = !oldInvert;
            if (!TrySubmitSingleCameraRequest())
                _mirrorCamera.Render();
        }
        catch (Exception e)
        {
            Log($"ForceRenderOnce failed: {e}");
        }
        finally
        {
            try
            {
                GL.invertCulling = oldInvert;
                if (_mirrorCamera != null)
                {
                    _mirrorCamera.ResetWorldToCameraMatrix();
                    _mirrorCamera.ResetProjectionMatrix();
                    _mirrorCamera.ResetCullingMatrix();
                }
            }
            finally
            {
                try
                {
                    OnPostDraw?.Invoke(this);
                }
                finally
                {
                    try
                    {
                        _endCameraRenderingCallbacks?.Invoke(default, _mirrorCamera);
                    }
                    finally
                    {
                        try
                        {
                            LifecycleOwner?.RestoreMirrorActorLayers(_mirrorCamera);
                        }
                        finally
                        {
                            try
                            {
                                // Preserve a parent ordinary-camera scope during a nested mirror draw.
                                while (_mirrorObjectLayers.Count > receiverLayerDepth)
                                    ResetMirrorObjectLayer();
                            }
                            finally
                            {
                                try
                                {
                                    GL.invertCulling = oldInvert;
                                }
                                finally
                                {
                                    _isRenderingNow = false;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private void Log(string msg)
    {
        if (_logDebug)
            Debug.Log($"[MirrorReflection] {name}: {msg}", this);
    }

    public void SetBaseCamera(Camera cam)
    {
        _baseCamera = cam;
        _baseCameraTransform = cam != null ? cam.transform : null;
    }

    public void SetFovFactorGetter(Func<float> getter)
    {
        _getBaseCameraFovFactor = getter;
    }
}
}

