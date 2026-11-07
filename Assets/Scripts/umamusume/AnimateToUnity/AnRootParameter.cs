using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimateToUnity
{
    [Serializable]
    public partial class AnRootParameter : AnScriptableObject
    {
        public string _rootMotionID;
        public string _layerName;
        public string _sortLayerName;
        public int _sortOffset;
        public int _stencilRefOffset;
        public AnStencilCompareFuncTypes _stencilCompareFunc;
        public float _depthOffset;
        public float _scaleOffset;
        public float _scaleValue;
        public bool _drawTextLater;
        public float _baseFrameRate;
        public float _baseNullSize;
        public AnScreenCastTypes _screenCastType;
        public float _baseCameraSize;
        public bool _fitScreen;
        public Vector2 _baseScreenSize;
        public Vector2 _baseScreenFixSize;
        public Vector2 _screenReferenceSize;
        public bool _useCustomMesh;
        public float _colliderThickness;
        public List<AnRootLocalizeParameter> _localizeParameterList;
        public AnMotionParameterGroup _motionParameterGroup;
        public float _oneFrameTime;
        public Vector2 _baseScreenUsingSize;
    }
}
