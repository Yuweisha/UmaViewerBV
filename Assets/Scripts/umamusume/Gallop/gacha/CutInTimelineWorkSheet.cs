using System;
using Gallop;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.CutIn.Cutt
{
    // Serialization bindings for the CutIn bundle. Playback lives in the viewer.
    public sealed class CutInTimelineWorkSheet : ScriptableObject
    {
        public bool _enableAtRuntime;
        public bool IsSubEmitterUse;
        public Track<EventKey> _eventKeys;
        public CharacterTrack[] _characterList;
        public Track<CameraMotion> _cameraMotionKeys;
        public CameraTargetTrack _cameraTargetCharaKeys;
        public Track<CameraParameters> _cameraParamKeys;
        public NamedTrack<SoundKey>[] _soundList;
        public BackgroundGroup[] _bgList;
        public EffectGroup[] _effectList;
        public NamedTrack<PostEffectKey>[] _postEffectList;
        public EnvironmentTrack _environment;
        public NamedTrack<AdditionalLightKey>[] AdditionalLightList;
        public Track<MultiLightShadowKey> MultiLightShadowList;
        public Track<CameraBackgroundKey> CameraBackGroundKeys;
        public MaskCameraTrack MaskCameraDataList;
        public PropGroup[] CharaPropList;
        public MonitorTrack[] _monitorCameraList;
        public MiniCharacterTrack[] MiniCharacterDataList;
        public AudienceTrack[] AudienceList;
        public NamedTrack<CyalumeKey>[] ChoreographyCyalumeList;
        public CrowdGroup[] CyalumeControlList, MobControlList;
        public SceneCardGroup[] SupportCardList;
        public Track<TimeScaleKey> _timeScaleList;

        [Serializable]
        public class Track<T> { public T[] thisList; }

        [Serializable]
        public class SceneCardGroup
        {
            public Track<SceneCardKey> _keys;
        }

        [Serializable]
        public class SceneCardKey : Key
        {
            public Interpolation[] _params;
            public bool IsVisible, UseMask;
            public Vector3 Position, Angle, Scale;
        }

        [Serializable]
        public class TimeScaleKey : Key
        {
            public Interpolation[] _params;
            public FloatValue _timeScaleSetting;
        }

        [Serializable]
        public class CyalumeKey : Key
        {
            public int Bpm;
            public Texture2D CyalumeTexture;
        }

        [Serializable]
        public class CrowdGroup
        {
            public CrowdGroupTrack _keys;
        }

        [Serializable]
        public class CrowdGroupTrack : Track<CrowdGroupKey>
        {
            public int _groupId;
        }

        [Serializable]
        public class CrowdGroupKey : Key
        {
            public Interpolation[] _params;
            public Vector3 Position, Angle, Scale;
        }

        [Serializable]
        public class EnvironmentTrack
        {
            public FogTrack _fogKeys;
            public Track<LightKey> _lightKeys;
            public Track<ReflectionKey> _reflectionKeys;
        }

        [Serializable]
        public class FogTrack : Track<FogKey>
        {
            public bool IsResetFogParam;
        }

        [Serializable]
        public class ReflectionKey : Key
        {
            public bool _isEnable;
            public MirrorReflection.Direction _direction;
        }

        [Serializable]
        public class AudienceTrack
        {
            public int ObjectIndex;
            public Track<AudienceKey> _keys;
        }

        [Serializable]
        public class AudienceKey : Key
        {
            public int _attribute;
            public Interpolation[] _params;
            public Vector3 Position, Rotate, Scale;
            public Color CyalumeColor, CyalumeGlowColor;
            public float CyalumeGlowColorPower, CyalumeMaskRadius;
            public int AnimationSetting, AnimationRootIndex, AnimationBodyRegion, AnimationCategory, AnimationIndex;
            public WrapMode AnimationWrapMode;
            public float AnimationSpeed, AnimationOffsetTime;
        }

        [Serializable]
        public class LightKey : Key
        {
            public Interpolation[] _params;
            public bool _isEnable, _isDisable;
            public VectorValue _angle;
            public ColorValue _color;
        }

        [Serializable]
        public class AdditionalLightKey : Key
        {
            public Interpolation[] _params;
            public bool IsEnable;
            public int Type;
            public Vector3 Position, Rotate;
            public float Range;
            public int SpotAngle, IndirectMultiplier;
            public LightShadows ShadowType;
            public float Strength, Bias, NormalBias, NearPlane;
        }

        [Serializable]
        public class MultiLightShadowKey : Key
        {
            public Interpolation[] _params;
            public bool IsUseParam, IsUseShadowFront, IsShadowNearStart;
            public Vector3 ShadowCenterPosition, ShadowForward;
            public Color ShadowStartColor, ShadowEndColor, FillColor;
            public float ShadowFadeStart, ShadowFadeLength, ShadowDistance;
        }

        [Serializable]
        public class CameraBackgroundKey : Key
        {
            public Interpolation[] _params;
            public Color BackGroundColor;
            public bool IsOrthoSize;
            public float OrthoSize;
        }

        [Serializable]
        public class MaskCameraTrack : Track<MaskCameraKey>
        {
            public Texture2D MaskTexture;
            public int MaskWidth = 256, MaskHeight = 256;
        }

        [Serializable]
        public class MaskCameraKey : Key
        {
            public Interpolation[] _params;
            public bool IsEnable;
            public int RenderQueue;
            public float FieldOfView, Alpha, Cuttoff;
            public Vector3 CameraPosition, CameraAngle, Position, Angle, Scale;
            public Texture2D MaskTexture;
        }

        [Serializable]
        public class MonitorTrack
        {
            public Track<CameraMotion> MotionKeys;
            public Track<CameraParameters> CameraParamKeys;
            public CameraTargetTrack CameraTargetKeys;
            public Track<MonitorEnableKey> MonitorCameraEnableKeys;
        }

        [Serializable]
        public class MonitorEnableKey : Key
        {
            public bool _isEnable;
        }

        [Serializable]
        public class PropGroup
        {
            public string _name, SearchId;
            public PropTrack _keys;
        }

        [Serializable]
        public class PropTrack : Track<PropKey>
        {
            public GameObject _charaPropPrefab;
            public int AttachCharacterIndex;
            public bool IsUseTemporaryObjectParent, _enableRandomEntry;
            public RandomPropEntry[] _randomEntryArray;
        }

        [Serializable]
        public class RandomPropEntry
        {
            public int _propId, _propSubId, _weight;
        }

        [Serializable]
        public class PropKey : SpatialKey
        {
            public Interpolation[] _params;
            public bool IsVisible, IsPlay, IsStop, IsAttach, IsDetach;
            public bool IsDetachStayTransform, IsAttachPreLateUpdate;
            public string AttachLocator, AttachLocatorSearchId;
            public int AttachIndex;
            public bool IsUsePosition, IsUseRotation, IsUseScale, IsUseWorldSpace, IsUseAttachScale;
            public bool IsReferenceAttachCharaVisibleState;
            public bool IsUseTargetGender;
            public int TargetGender;
            public bool IsSilhouette;
            public bool IsNormalizeNormal;
            public BoolValue IsDepthWriteAlphaMesh;
            public bool IsUpdatePostEffect, IsIgnoreColorCorrectionLayer;
            public bool IsUseFillColor, IsEmissiveColor, IsEmissiveIntensity, IsUpdateOutline;
            public Color FillColor, EmissiveColor, OutlineColor;
            public float EmissiveIntensity, OutlineWidth;
            public bool IsEmissiveSoftEdge, IsEmissiveCenter;
            public float EmissiveRimPower, EmissiveRimIntensity;
            public bool IsCharaColor, IsToonParam, IsRimColor, IsToonColor, IsUpdateStencil;
            public int StencilValue;
            public ColorValue CharaColor, RimColor, ToonDarkColor, ToonBrightColor;
            public FloatValue ToonStep, ToonFeather, RimStep, RimFeather, RimSpecRate, RimShadow;
            public VectorValue LightDir;
            public bool IsLightDirByAttachChara;
            public bool IsModelGradient;
            public EnumValue GradientInputMode, GradientBlendMode;
            public ColorValue GradientTopColor, GradientBottomColor;
            public VectorValue GradientStartWorldPosition, GradientEndWorldPosition;
            public StringValue GradientStartNodeName, GradientEndNodeName;
            public VectorValue GradientStartNodeOffset, GradientEndNodeOffset;
            public FloatValue GradientScale, GradientOffset, GradientBlendAlpha;
            public BoolValue IsGradientOutline, IsGradientScreenMode;
            public AnimationClip _clip;
            public FloatValue _motionHeadTime, _playLength, _playSpeed;
            public bool _useRandomAnimationClip;
            public AnimationClip[] _randomAnimationClipArray;
        }

        [Serializable]
        public class StringValue
        {
            public bool _isValid;
            public string _value;
        }

        [Serializable]
        public class FogKey : Key
        {
            public Interpolation[] _params;
            public ColorValue _color;
            public Vector4Value _minDistance;
            public Vector4Value _maxDistance;
            public FloatValue _maxDensity;
            public FloatValue _maxHeight;
        }

        [Serializable]
        public class Vector4Value
        {
            public bool _isValid;
            public Vector4 _value;
        }

        [Serializable]
        public class NamedTrack<T>
        {
            public string _name;
            public Track<T> _keys;
        }

        [Serializable]
        public class BackgroundGroup
        {
            public string _name;
            public string SearchId;
            public BackgroundTrack _keys;
        }

        [Serializable]
        public class BackgroundTrack : Track<BackgroundKey>
        {
            public int _loadedType;
            public int _positionIndex;
            public GameObject _prefab;
        }

        [Serializable]
        public class EffectGroup
        {
            public string _name;
            public EffectTrack _keys;
        }

        [Serializable]
        public class EffectTrack : Track<EffectKey>
        {
            public string _searchId;
            public bool _isValidTimeScaleCurve;
            public bool IsEnableCharaSubColor;
            public bool IsFlash;
            public string FlashName;
            public GameObject _prefab;
            public BoolValue IsOverrideSubEmitter;
        }

        [Serializable]
        public class Key { public int _frame; }

        [Serializable]
        public class EventKey : Key { public Event[] eventList; }

        [Serializable]
        public class Event
        {
            public int eventId;
            public string serializedParamter;
        }

        [Serializable]
        public class FloatValue
        {
            public bool _isValid;
            public float _value;

            public float Or(float fallback) => _isValid ? _value : fallback;
        }

        [Serializable]
        public class VectorValue
        {
            public bool _isValid;
            public Vector3 _value;

            public Vector3 OrZero => _isValid ? _value : Vector3.zero;
        }

        [Serializable]
        public class SpatialKey : Key
        {
            public Vector3 _pos;
            public Vector3 _rot;
            public Vector3 _scale;
            public bool _usePosition;
            public bool _useRotation;
        }

        [Serializable]
        public class Motion : Key
        {
            public float _motionHeadTime;
            public float _playLength;
            public float _playSpeed;
            public bool _loop;
            public AnimationClip _clip;
        }

        [Serializable]
        public class CameraMotion : Motion { public bool _enable; }

        [Serializable]
        public class CameraTargetTrack : Track<CameraTarget>
        {
            public int _targetCharacterId = -1;
        }

        [Serializable]
        public class CameraTarget : Key
        {
            public bool _useTargetCharacter;
            public int _targetCharacterId;
            public bool _individualScale;
            public int[] _targetCharacterIndices;
        }

        [Serializable]
        public class CameraParameters : Key
        {
            public Interpolation[] _params;
            public FloatValue _nearClipPlane;
            public FloatValue _farClipPlane;
            public bool _useCameraOpaqueTexture;
            public FloatValue _cameraOpaqueTextureSizeRate;
            public VectorValue PositionOffset;
            public VectorValue RotationOffset;
        }

        [Serializable]
        public class CharacterTrack
        {
            public CharacterKeys _characterKeys;
            public Track<CharacterMotion> _characterMotionKeys;
            public Track<CharacterColor>[] _characterColorKeys;
            public CharacterPartTrack[] CharacterPartsKeyArray;
            public Track<CharacterShadowKey>[] _characterShadowKeys;
            public Track<CharacterWindKey>[] CharacterWindKeysArray;
            public Track<CharacterSweatKey>[] _characterSweatKeys;
            public Track<CharacterDirtKey>[] CharacterDirtKeyArray;
            public Track<CharacterAuraKey>[] CharacterAuraKeyArray;
            public Track<HologramKey>[] CharacterHologramKeyArray;
            public Track<ScreenMappingKey>[] CharacterScreenMappingKeyArray;
            public Track<CharacterScaleKey>[] CharacterScaleFactorKeyArray;
        }

        [Serializable]
        public class CharacterScaleKey : Key
        {
            public Interpolation[] _params;
            public FloatValue ScaleFactor;
        }

        [Serializable]
        public class MiniCharacterTrack
        {
            public MiniCharacterKeys CharacterKeys;
            public Track<CharacterMotion> CharacterMotionKeys;
            public Track<MiniCharacterColor>[] CharacterColorKeys;
        }

        [Serializable]
        public class MiniCharacterKeys : Track<MiniCharacterKey>
        {
            public int _selectCharaId, _selectClothId, _selectHeadId;
            public bool _isUseDressDataHeadModelSubId;
        }

        [Serializable]
        public class MiniCharacterKey : SpatialKey
        {
            public bool IsVisible, IsUsePosition, IsUseRotation, IsUseScale;
            public bool IsUseUpdateStencil;
            public int UpdateStencilValue;
        }

        [Serializable]
        public class MiniCharacterColor : Key
        {
            public Interpolation[] _params;
            public ColorValue CharaColor, OutlineColor;
            public FloatValue ColorPower, OutlineWidthPower;
            public EnumValue OutlineColorBlendType;
        }

        [Serializable]
        public class CharacterWindKey : Key
        {
            public Interpolation[] _params;
            public CySpringWindParam _windParam;
        }

        [Serializable]
        public class CharacterSweatKey : Key
        {
            public Interpolation[] _params;
            public SweatLocator[] _sweatInfo;
        }

        [Serializable]
        public class SweatLocator
        {
            public bool _isEnable;
            public VectorValue _offset;
            public ColorValue _color;
            public FloatValue _colorPower, _strength, _threshHold;
        }

        [Serializable]
        public class CharacterDirtKey : Key
        {
            public Interpolation[] _params;
            public FloatValue DirtEnergyR, DirtEnergyG, DirtEnergyB;
            public ColorValue DirtColor, DirtRimSpecularColor, DirtToonColor;
        }

        [Serializable]
        public class CharacterAuraKey : Key
        {
            public Interpolation[] _params;
            public bool IsEnable;
            public float Fresnel, Threshold, Intensity, Spread, Distortion, Fluctuation, Speed;
            public Color Color;
            public Vector3 OffsetViewDirection;
        }

        [Serializable]
        public class HologramKey : Key
        {
            public Interpolation[] _params;
            public bool IsEnable, IsHologramOnly, VoxelEnable;
            public float FlickerSpeed, FlickerIntensity;
            public Texture2D NoiseTex;
            public Vector3 Direction;
            public float ScanTiling, ScanSpeed, RimPower, RimIntensity, Grain, GlowTiling, GlowSpeed;
            public float GlitchSpeed, GlitchIntensity, BaseEmission, HologramAlpha, HologramGlowIntensity;
            public int GlitchType;
            public Color HologramBaseColor, HologramRimColor;
            public float VoxelSmoothness, VoxelIntensity, GlitchNoiseIntensity;
        }

        [Serializable]
        public class ScreenMappingKey : Key
        {
            public Interpolation[] _params;
            public bool AppliesNoseShadow, AppliesMouthShadow;
            public bool UsesBodyParam, UsesFaceParam, UsesEyeParam, UsesHairParam, UsesTailParam;
            public bool UsesAttachmentParam, UsesHeadObjParam;
            public ScreenMappingParam AllParam, BodyParam, FaceParam, EyeParam, HairParam, TailParam, AttachmentParam, HeadObjParam;
        }

        [Serializable]
        public class ScreenMappingParam
        {
            public bool IsEnabled, IsReversePattern;
            public Color Color;
            public int BlendMode, PatternType;
            public float ColorPower, PatternInterval, PatternLength;
        }

        [Serializable]
        public class CharacterShadowKey : Key
        {
            public Interpolation[] _params;
            public int _shadowType;
            public FloatValue _angle;
            public VectorValue _angles;
            public ColorValue _color;
            public FloatValue _loopTime;
            public FloatValue _shadowPower;
            public VectorValue _offset;
            public VectorValue _scale;
            public IntValue _renderQueue;
            public bool _hipLink;
            public bool _heightLink;
        }

        [Serializable]
        public class IntValue
        {
            public bool _isValid;
            public int _value;
        }

        [Serializable]
        public class CharacterPartTrack : Track<CharacterPartKey>
        {
            public string RendererName;
        }

        [Serializable]
        public class CharacterPartKey : Key
        {
            public bool IsVisible;
            public bool IsApplyToSpecificCharaId;
            public int SpecificCharaId;
        }

        [Serializable]
        public class CharacterKeys : Track<CharacterKey>
        {
            public int _characterType;
            public int _charaIndex;
            public int _selectCharaId, _selectClothId, _selectHeadId;
            public bool _isUseDressDataHeadModelSubId;
        }

        [Serializable]
        public class ColorValue
        {
            public bool _isValid;
            public Color _value;

            public Color Or(Color fallback) => _isValid ? _value : fallback;
        }

        [Serializable]
        public class Interpolation
        {
            public int _interpolateType;
            public AnimationCurve _curve;
            public int _easingType;
            public bool _isUseTimeUpdate;

            internal static float NativeWeightBetween(Key previous, Key next, Interpolation[] parameters,
                int channel, int frame, float seconds = 0f)
            {
                var interpolation = parameters != null && channel < parameters.Length ? parameters[channel] : null;
                if (interpolation?._interpolateType is not > 0)
                    return 0f;

                // Linear interpolation reads channel zero. Curves read the requested channel.
                bool useTime = interpolation._interpolateType switch
                {
                    1 => parameters[0]?._isUseTimeUpdate == true,
                    2 => interpolation._isUseTimeUpdate,
                    _ => false
                };
                return interpolation.WeightBetween(previous, next, useTime ? seconds * 30f : frame);
            }

            public float WeightBetween(Key previous, Key next, float frame)
            {
                float progress = Mathf.InverseLerp(previous._frame, next._frame, frame);
                return _interpolateType switch
                {
                    2 when _curve != null => _curve.Evaluate(progress),
                    3 => LiveTimelineEasing.GetValue((LiveTimelineEasing.Type)_easingType,
                        frame - previous._frame, 0f, 1f, next._frame - previous._frame),
                    _ => progress
                };
            }
        }

        [Serializable]
        public class CharacterColor : Key
        {
            public Interpolation[] _params;
            public VectorValue LightDir;
            public EnumValue UseOriginalDirectionalLight;
            public VectorValue OriginalDirectionalLightDir;
            public ColorValue CharaColor;
            public FloatValue ColorPower;
            public ColorValue ToonBrightColor;
            public ColorValue ToonDarkColor;
            public ColorValue RimColor;
            public FloatValue RimStep;
            public FloatValue RimFeather;
            public FloatValue RimSpecRate;
            public FloatValue GlobalRimShadowRate;
            public FloatValue RimHorizonOffset, RimVerticalOffset, RimHorizonOffset2, RimVerticalOffset2;
            public ColorValue RimColor2;
            public FloatValue RimStep2, RimFeather2, RimSpecRate2, GlobalRimShadowRate2;
            public FloatValue VertexColorToonPower;
            public FloatValue ToonStep, ToonFeather, EnvRate, EnvBias, Silhouette, Saturation;
            public ColorValue SpecularColor;
            public FloatValue SpecularPower;
            public BoolValue RestoreSpecularColor, RestoreSpecularPower;
            public FloatValue EmissiveIntensity, EmissiveRimPower, EmissiveRimIntensity;
            public BoolValue IsEmissiveCenter;
            public BoolValue IsUseModelGradient, IsGradientScreenMode, IsGradientOutline;
            public EnumValue GradientInputMode, GradientBlendMode;
            public ColorValue GradientTopColor, GradientBottomColor;
            public VectorValue GradientStartWorldPosition, GradientEndWorldPosition;
            public StringValue GradientStartNodeName, GradientEndNodeName;
            public VectorValue GradientStartNodeOffset, GradientEndNodeOffset;
            public FloatValue GradientScale, GradientOffset, GradientBlendAlpha;
            public FloatValue OutlineWidthPower;
            public ColorValue OutlineColor;
            public EnumValue OutlineColorBlendType;
            public FloatValue NosePretenseThreshold;
            public FloatValue EyeToonStep, EyeToonFeather, EyeSaturation, EyeSilhouette;
            public FloatValue CheekPretenseThreshold;
            public FloatValue CylinderBlend, HairNormalBlend;
        }

        [Serializable]
        public class EnumValue
        {
            public bool _isValid;
            public int _intValue;
        }

        [Serializable]
        public class CharacterKey : SpatialKey
        {
            public Interpolation[] _params;
            public bool[] _enableCySpringList;
            public string[] _targetCySpringNameList;
            public bool _isAffectOwnerRotation = true;
            public float CySpringRate = 1f;
            public bool _visible;
            public bool IsEmissiveColor;
            public Color EmissiveColor;
            public bool IsMayuControl, IsHideMayu;
            public bool IsEyePupli = true;
            public bool IsPartsControl, IsPartsScale;
            public bool IsValidTransformExpression, IsEnableBodyExpression, IsEnableHeadExpression;
            public bool IsChangeLayer;
            public int Layer;
            public bool _useAddPos;
            public Vector3 _addPos;
            public bool _isEnableHeightScale;
            public float _heightScale;
        }

        [Serializable]
        public class CharacterMotion : Motion
        {
            public AnimationClip _bodyClip;
            public AnimationClip[] _bodyClips;
            public string _bodyClipName;
            public AnimationClip _positionClip;
            public BoolValue _isApplyPositionClipScale;
            public DrivenKeyAnimation _facialClip;
            public DrivenKeyAnimation _earClip;
            public FacialAdditiveMotionAsset _facialAdditiveMotionAsset;
            public bool IsEyeHighlightPlayTime, IsNeedUpdateEyeHighlightFromTimeline, ResetEyeHighlightAnimation;
            public FloatValue _bodyClipHeadTime;
            public FloatValue _bodyClipPlaySpeed;
            public FloatValue _positionClipHeadTime;
            public FloatValue _positionClipPlaySpeed;
            public FloatValue _facialClipHeadTime;
            public FloatValue _facialClipPlaySpeed;
            public FloatValue _earClipHeadTime;
            public FloatValue _earClipPlaySpeed;
            public bool _isResetCyspring;
        }

        [Serializable]
        public class BoolValue
        {
            public bool _isValid;
            public bool _value;
        }

        [Serializable]
        public class BackgroundKey : SpatialKey
        {
            public Interpolation[] _params;
            public bool _visible;
            public bool _useScale;
            public bool IsOrtho;
            public LayerValue LayerChange;
            public bool _useFillColor, _useAddColor;
            public Color _fillColor, _addColor;
            public FloatValue MirrorReflectionRate, MirrorClipPlaneOffset;
            public Vector4Value MirrorDistortionTileOffset, MirrorDistortionPower;
            public bool IsEnableMirrorBlur, IsToonMirror, IsEnableMirrorMsaa;
            public int MirrorBlurDownSample;
            public GrassData[] GrassList;
            public int GrassBaseFrame;
            public bool _isPlay;
            public bool _isValidClip;
            public bool IsUnbindTime;
            public bool IsTimeAnimationLoop;
            public AnimationClip _clip;
            public FloatValue _motionHeadTime;
            public FloatValue _playLength;
            public FloatValue _playSpeed;
            public FloatValue PoolWaterClipPlaneOffset;
            public FloatValue PoolWaterPlaneOffsetY;
            public FloatValue PoolWaterWaveScale;
            public FloatValue PoolWaterDistanceFadeByNear;
            public FloatValue PoolWaterDistanceFadeByFar;
            public FloatValue PoolWaterDistanceFadeByAlpha;
            public int MonitorCameraIndex;
            public bool IsUpdateProjectionMatrixPreRender;
            public bool IsEnabledMonoBehaviourUpdate;
            public bool UseMaskingFade;
            public float MaskingFadeRate;
            public ChildVisibility[] _childVisibilityArray;
            public bool IsLoadExternalTexture;
            public string ExternalTextureMateralName;
        }

        [Serializable]
        public class GrassData
        {
            public Material TargetMaterial;
            public Texture WindDirTexture;
            public Vector2 WindParam;
            public float BaseTimeScale, WindScale;
        }

        [Serializable]
        public class LayerValue
        {
            public bool _isValid;
            public int _intValue;
        }

        [Serializable]
        public class ChildVisibility
        {
            public string GameObjectName;
            public bool IsVisible;
        }

        [Serializable]
        public class EffectKey : SpatialKey
        {
            public Interpolation[] _params;
            public string PlayLabel;
            public bool _useFillColor;
            public Color _fillColor;
            public bool _useEmissive;
            public Color _emissiveColor;
            public bool _play;
            public bool _stop;
            public bool _hide;
            public bool IsPause;
            public bool IsUseFixedTime;
            public bool IsUseFixedTimeForShort;
            public bool IsPlaySimulateTime;
            public bool _useNonPostprocessLayer, _useUiLayer, _useUi3DLayer;
            public bool _isOrtho, _useColorCorrectionLayer, IsUseMaskLayer;
            public bool UIIsScaleByAspectRatio;
            public int ScreenAlignmentValue;
            public bool IsOrderInLayerOverride, IsUpdateStencil;
            public bool IsApplyGameObjectLayerToEffect, IsUpdateZIgnore, IsUpdateCulling, IsUpdateOutline;
            public int ZIgnoreParam, CullingMode;
            public float OutlineWidth;
            public Color OutlineColor;
            public int EffectRendererOrderInLayerOffset, StencilValue, StencilCompare, StencilOperation;
            public bool _useScale;
            public bool _attach;
            public bool _detach;
            public int _attachCharacterIndex;
            public string _attachLocator, _attachLocatorSearchId;
            public bool _attachUsePos;
            public bool _attachUsePosX;
            public bool _attachUsePosY;
            public bool _attachUsePosZ;
            public bool _attachUseRot;
            public bool _attachUseScale;
            public bool _useLocalRotation;
            public Vector3 _localRot;
            public AnimationClip _clip;
            public FloatValue _motionHeadTime, _playLength;
        }

        [Serializable]
        public class PostEffectKey : Key
        {
            public Interpolation[] _params;
            public bool _fadeEnable;
            public Color _fadeColor;
            public bool _bloomEnable;
            public float _bloomThreshHold;
            public float _bloomIntensity;
            public float _bloomBlurSize;
            public float _bloomDofWeight;
            public int BloomBlendMode;
            public bool _diffusion;
            public float _diffusionContrast, _diffusionSaturation, _diffusionBright, _diffusionBlurSize, _diffusionThreshold;
            public int MoveBlurType;
            public int RadialBlurDownsample;
            public int RadialBlurIteration;
            public Vector2 RadialBlurOffset;
            public Vector2 RadialBlurEllipseDir;
            public float RadialBlurStartArea;
            public float RadialBlurEndArea;
            public float RadialBlurPower;
            public float RadialBlurRollEulerAngles;
            public bool IsEnabledDepth, IsEnabledDepthCancelRect, IsExpandDepthCancelRect;
            public float DepthPowerFront, DepthPowerBack, DepthCancelBlendLength;
            public Vector4 DepthCancelRect;
            public PostFilmLayer[] _postFilmParam;
            public bool _colorCorrectionEnable;
            public bool IsColorCorrectionIgnoreDraw;
            public uint ColorCorrectionIgnoreCharacterFlags;
            public float _colorCorrectionSaturation;
            public AnimationCurve _colorCorrectionRedChannel, _colorCorrectionGreenChannel, _colorCorrectionBlueChannel;
            public bool _dofEnable, _oldDof, IsPointBallBlur;
            public int _dofFocalType, _dofFocalTargetType, _dofQuality, DofBlurType;
            public Vector3 _dofFocalPosition;
            public float _dofFocalTarget, _dofFocalSize, _dofForegroundSize, _dofMaxBlurSpread, _dofSmoothness;
            public float BallBlurPowerFactor, BallBlurBrightnessThreshhold, BallBlurBrightnessIntensity, BallBlurSpread;
            public bool _sunShaftEnable, _sunShaftsBoarderClear;
            public Vector3 _sunShaftsPosition;
            public Color _sunShaftsColor;
            public int _sunShaftsScreenBlendMode, SunShaftsResolution;
            public float _sunShaftsBlurRadius, _sunShaftsPower, _sunShaftsBlackLevel;
            public float _sunShaftsCenterBrightness, _sunShaftsCenterMultiplex, _sunShaftsKomorebiRate;
            public float _sunShaftsColorLerpRate, _sunShaftsEffectColorPower, _sunShaftsScreenColorPower;
            public bool DistanceFog, HeightFog, UseRadialDistance;
            public Color FogColor;
            public int SceneFogMode;
            public float FogHeight, FogHeightDensity, StartDistance, SceneFogDensity, SceneFogStart, SceneFogEnd;
            public TiltShiftData TiltShiftParam;
            public ChromaticData ChromaticAberrationParam;
            public LensDistortionData LensDistortionParam;
            public HatchingData HatchingParam;
            public FluctuationData FluctuationParam;
            public TransmittedLightData TransmittedLightParam;
            public FilmRollData FilmRollParam;
        }

        [Serializable]
        public class TiltShiftData
        {
            public int Mode, Quality, Downsample;
            public float BlurArea, MaxBlurSize, Roll;
            public Vector2 Offset;
        }

        [Serializable]
        public class ChromaticData
        {
            public bool IsEnable;
            public Vector2 RedOffset, GreenOffset, BlueOffset;
            public float Power, Clip;
            public int Type;
        }

        [Serializable]
        public class LensDistortionData
        {
            public float Intensity, IntensityX, IntensityY, CenterX, CenterY, Scale;
        }

        [Serializable]
        public class HatchingData
        {
            public int BlendMode;
            public Texture2D NoiseTexture;
            public float BlendAlpha, StrokeTiling, StrokeHeight, StrokeAngle, StrokeLength;
            public Vector2 CircleMaskOffset, CircleMaskStretchRatio;
            public float CircleMaskSize, CircleMaskAngle, CircleMaskInnerRatio, CircleMaskInnerEffect;
        }

        [Serializable]
        public class FluctuationData
        {
            public bool IsEnable;
            public Vector2 MoveDirection;
            public float MovePower, Power, DepthClip;
        }

        [Serializable]
        public class TransmittedLightData
        {
            public bool IsEnable;
            public int Iterations, BloomBlendMode;
            public float Intensity, Threshold, BlurSpread;
        }

        [Serializable]
        public class FilmRollData
        {
            public bool IsEnable, IsNoiseRandom;
            public Vector2 RollPosition, RollMaskSize, RollMaskFadeSize, NoiseRange, ScanlineBlurSize;
            public Color RollMaskColor, NoiseColor;
            public float NoisePower, ScanLineSpeed, ScanLineSize, ScanLinePower;
            public int EnqueueTimiing;
        }

        [Serializable]
        public class PostFilmLayer
        {
            public int _blendMode;
            public bool _inverseVignette;
            public float _depthPower, DepthClip, _postFilmPower, RollAngle;
            public Vector2 _postFilmOffsetParam, FilmScale;
            public Vector4 _postFilmOptionParam;
            public Color[] _color;
        }

        [Serializable]
        public class SoundKey : Key
        {
            public Sound _bgmData;
            public Sound _seData;
            public Sound _envData;
            public Voice _voiceData;
        }

        [Serializable]
        public class Sound
        {
            public bool _enable;
            public string _sheetName;
            public string _id;
            public bool _play;
            public bool _isVolume;
            public bool _loop;
            public bool _keepBgm;
            public float _volume;
            public int _fadeDurationMilliseconds;
            public int _stopFadeDurationMilliseconds;
        }

        [Serializable]
        public class Voice
        {
            public bool _enable;
            public bool _play;
            public int _charaId;
            public int _type;
        }
    }
}
