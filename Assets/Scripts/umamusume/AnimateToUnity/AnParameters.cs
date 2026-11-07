// Original serialized A2U parameter contracts used by Live Flash assets.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AnimateToUnity
{
    [Serializable]
    public partial class AnScriptableObject : ScriptableObject
    {
        public string _id;
    }

    public enum AnStencilCompareFuncTypes
    {
        None = -1,
        Disabled = 0,
        Never = 1,
        Less = 2,
        Equal = 3,
        LessEqual = 4,
        Greater = 5,
        NotEqual = 6,
        GreaterEqual = 7,
        Always = 8
    }

    public enum AnScreenCastTypes
    {
        OthographicCamera = 0,
        ExpandBaseCanvas = 1,
        ShrinkBaseCanvas = 2,
        WidthBaseCanvas = 3,
        HeightBaseCanvas = 4,
        ConstantPixelCanvas = 5,
        Raw = 10
    }

    [Serializable]
    public partial class AnRootLocalizeParameter
    {
        public string _localizeTarget;
        public List<string> _fontNameFromCommonList;
    }

    [Serializable]
    public partial class AnMotionParameterGroup
    {
        public List<AnMotionParameter> _motionParameterList;
    }

    [Serializable]
    public partial class AnMotionParameter
    {
        public string _id;
        public string _name;
        public AnLabelParameter[] _labelParamList;
        public List<AnObjectParameter> _objectParamList;
        public List<AnPlaneParameter> _planeParamList;
        public List<AnTextParameter> _textParamList;
    }

    [Serializable]
    public partial class AnLabelParameter
    {
        public string _name;
        public Vector2 _timeRange;
        public string _nextLabel;
        public AnObjectControlInfoParameter[] _objectControlInfoParamList;
        public int _Index;
        public int _nextIndex;
        public Hashtable _actionStartTable;
        public Hashtable _actionLoopTable;
        public Hashtable _actionEndTable;
        public Hashtable _flActionStartTable;
        public Hashtable _flActionLoopTable;
        public Hashtable _flActionEndTable;
    }

    [Serializable]
    public partial class AnObjectControlInfoParameter
    {
        public string _targetName;
        public float _startTime;
        public string _targetLabel;
        public float _targetTime;
        public bool _targetIsStop;
        public AnObjectControlInfoTypes _objectControlInfoType;
    }

    public enum AnObjectControlInfoTypes
    {
        None = 0,
        MotionPlayByLabel = 1,
        MotionPlayByTime = 2
    }

    [Serializable]
    public partial class AnObjectParameter : AnObjectParameterBase
    {
        public string _childMotionID;
        public AnMotionResetModeTypes _motionResetModeType;
    }

    [Serializable]
    public partial class AnObjectParameterBase
    {
        public string _objectName;
        public int _objectIndex;
        public AnObjectTypes _objectType;
        public AnBlendModeTypes _blendModeType;
        public Vector2 _timeRange;
        public Vector2 _size;
        public Vector3 _position;
        public Vector3 _positionOffset;
        public Vector3 _rotate;
        public Vector3 _scale;
        public Vector2 _shear;
        public Color _color;
        public Color _colorOffset;
        public bool _isStabilizeRotation;
        public AnPlaceAnchorTypes _placeAnchorType;
        public AnPlaceAnchorAttachTypes _placeAnchorAttachType;
        public AnPlaceAnchorScaleTypes _placeAnchorScaleType;
        public string _layerName;
        public string _sortLayerName;
        public int _sortOffset;
        public float _depthOffset;
        public int _stencilRef;
        public AnStencilCompareFuncTypes _stencilCompareFunc;
        public AnTimeModeTypes _timeModeType;
        public Vector2 _blurValue;
        public int _blurQuality;
        public int _blurPrecision;
        public List<AnKeyParameter> _positionKeyParamList;
        public List<AnKeyParameter> _positionOffsetKeyParamList;
        public List<AnKeyParameter> _rotateKeyParamList;
        public List<AnKeyParameter> _scaleKeyParamList;
        public List<AnKeyParameter> _shearKeyParamList;
        public List<AnKeyParameter> _colorKeyParamList;
        public List<AnKeyParameter> _colorOffsetKeyParamList;
        public List<AnKeyParameter> _blurKeyParamList;
        public AnCollisionTypes _collisionType;
        public AnCollisionParameter[] _collisionParamList;
        public List<int> _flagKeyList;
        public List<string> _flagValueList;
        public AnUIParameter _uiParameter;
        public AnTransformAnimationFlags _transformAnimationFlag;
        public AnColorAnimationFlags _colorAnimationFlag;
        public AnBlurAnimationFlags _blurAnimationFlag;
    }

    public enum AnObjectTypes
    {
        Motion = 0,
        Object = 1,
        Plane = 2,
        Text = 3,
        Mask = 4,
        AlphaMask = 5,
        StencilMask = 6,
        StencilAlphaMask = 7,
        Opaque = 8,
        ObjectMask = 9,
        ObjectAlphaMask = 10
    }

    public enum AnBlendModeTypes
    {
        Normal = 0,
        Add = 1,
        Sub = 2,
        Multiply = 3,
        HardLight = 4,
        Invert = 5
    }

    public enum AnPlaceAnchorTypes
    {
        Default = 0,
        TopLeft = 1,
        TopCenter = 2,
        TopRight = 3,
        MiddleLeft = 4,
        MiddleCenter = 5,
        MiddleRight = 6,
        BottomLeft = 7,
        BottomCenter = 8,
        BottomRight = 9
    }

    public enum AnPlaceAnchorAttachTypes
    {
        Default = 0,
        Edge = 1,
        Margin = 2
    }

    public enum AnPlaceAnchorScaleTypes
    {
        Default = 0,
        Fix = 1
    }

    public enum AnTimeModeTypes
    {
        Normal = 0,
        Sync = 1
    }

    [Serializable]
    public partial class AnKeyParameter
    {
        public List<Vector2> _keyList;
        public int _keyCount;
    }

    public enum AnCollisionTypes
    {
        None = 0,
        Square = 1,
        Circle = 2,
        Square2D = 3,
        Circle2D = 4
    }

    [Serializable]
    public partial class AnCollisionParameter
    {
        public AnCollisionTypes _collisionType;
        public Vector3 _offset;
        public float _scale;
        public bool _through;
    }

    [Serializable]
    public partial class AnUIParameter
    {
        public AnUITypes _uiType;
        public List<string> _parameterKeyList;
        public List<string> _parameterValueList;
    }

    public enum AnUITypes
    {
        None = 0,
        Button = 1,
        CheckButton = 2,
        ImageNumber = 3,
        ProgressBar = 4,
        ScrollBar = 5,
        SlideBar = 6,
        CheckButtonList = 7,
        TextScroll = 8,
        ObjectScroll = 9,
        ObjectScrollList = 10,
        UpDownArrow = 11
    }

    public enum AnTransformAnimationFlags
    {
        Position = 1,
        PositionX = 2,
        PositionY = 4,
        PositionZ = 8,
        PositionOffset = 16,
        PositionOffsetX = 32,
        PositionOffsetY = 64,
        PositionOffsetZ = 128,
        Rotate = 256,
        RotateX = 512,
        RotateY = 1024,
        RotateZ = 2048,
        Scale = 4096,
        ScaleX = 8192,
        ScaleY = 16384,
        ScaleZ = 32768,
        Shear = 65536,
        ShearX = 131072,
        ShearY = 262144
    }

    public enum AnColorAnimationFlags
    {
        Color = 1,
        ColorR = 2,
        ColorG = 4,
        ColorB = 8,
        ColorA = 16,
        ColorOffset = 32,
        ColorOffsetR = 64,
        ColorOffsetG = 128,
        ColorOffsetB = 256,
        ColorOffsetA = 512
    }

    public enum AnBlurAnimationFlags
    {
        Blur = 1,
        BlurX = 2,
        BlurY = 4
    }

    public enum AnMotionResetModeTypes
    {
        ResetAll = 0,
        None = 1,
        ResetLabel = 2
    }

    [Serializable]
    public partial class AnPlaneParameter : AnObjectParameterBase
    {
        public List<string> _textureNameList;
        public bool _fullNineSlice;
        public List<Color> _vertexColorList;
        public List<Vector2> _uvColorList;
        public List<Vector2> _uvAlphaList;
        public AnKeyParameter _textureKeyParam;
    }

    [Serializable]
    public partial class AnTextParameter : AnObjectParameterBase
    {
        public AnTextMeshTypes _textMeshType;
        public string _text;
        public string _fontName;
        public int _fontSize;
        public float _lineSpace;
        public TextAnchor _anchor;
        public FontStyle _fontStyle;
        public TextAlignment _alignment;
        public bool _useWrap;
        public bool _useFit;
        public Color32 _textColor;
        public Color32 _shadowColor;
        public float _shadowOffset;
        public float _shadowAngle;
        public Color32 _outlineColor;
        public int _outlineQuality;
        public float _outlineOffset;
        public string _gradationStartObjectName;
        public string _gradationEndObjectName;
    }

    public enum AnTextMeshTypes
    {
        Normal = 0,
        Shadow = 1,
        Outline = 2,
        ShadowAndOutline = 3
    }

    [Serializable]
    public partial class AnCustomMeshInfoParameter
    {
        public string _textureName;
        public AnPrimitiveMeshTypes _primitiveMeshType;
        public Mesh _customMesh;
        public Texture _textureColor;
        public Texture _textureAlpha;
        public Vector3 _positionOffset;
        public Vector3 _rotateOffset;
        public Vector3 _scaleOffset;
        public Vector2 _uvPositionOffset;
        public Vector2 _uvScaleOffset;
        public bool _cullingOn;
        public bool _invertNormal;
        public bool _keepMeshSize;
        public bool _keepMeshAspect;
        public float _marginTop;
        public float _marginButtom;
        public float _marginRight;
        public float _marginLeft;
    }

    public enum AnPrimitiveMeshTypes
    {
        None = 0,
        Sphere = 1,
        Cube = 2,
        Ring = 3,
        Cylinder = 4,
        Plane = 5
    }

    [Serializable]
    public partial class AnMeshInfoParameterGroup
    {
        public string _textureSetName;
        public Vector2 _textureSetSize;
        public Texture _textureSetColor;
        public Texture _textureSetAlpha;
        public AnColorTextureFormatTypes _textureSetColorFormat;
        public AnAlphaTextureFormatTypes _textureSetAlphaFormat;
        public AnTextureCombinationTypes _textureCombinationType;
        public List<AnMeshInfoParameter> _meshInfoParameterList;
    }

    public enum AnColorTextureFormatTypes
    {
        Compress = 0,
        RGB16Bit = 1,
        RGB24Bit = 2,
        None = 3
    }

    public enum AnAlphaTextureFormatTypes
    {
        Compress = 0,
        A8Bit = 1,
        None = 2
    }

    public enum AnTextureCombinationTypes
    {
        None = 0,
        ColorRGB_And_AlphaR = 1,
        ColorRGB_And_AlphaA = 2,
        ColorRGBA = 3
    }

    [Serializable]
    public partial class AnMeshInfoParameter
    {
        public string _textureName;
        public string _fixTextureName;
        public Vector2 _size;
        public Vector2 _offset;
        public Vector2 _uvSize;
        public Vector2 _uvOffset;
        public bool _rotated;
        public AnMeshTypes _meshType;
        public Vector4 _sliceRange;
        public Vector3[] _baseMeshVertices;
        public Vector3[] _baseCustomMeshVertices;
    }

    public enum AnMeshTypes
    {
        Normal = 0,
        NineSlice = 1,
        Mesh = 2,
        Model = 3
    }

    [Serializable]
    public partial class AnMeshParameterGroup
    {
        public List<AnMeshParameter> _meshParameterList;
    }
}
