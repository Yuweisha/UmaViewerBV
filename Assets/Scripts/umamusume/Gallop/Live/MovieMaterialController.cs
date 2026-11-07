using System;
using UnityEngine;

namespace Gallop.Live
{
    public sealed class MovieMaterialController
    {
        [Serializable]
        public sealed class MovieMaterialInfo
        {
            [SerializeField] Material _movieMaterial;
            [SerializeField] int _movieType;
            [SerializeField] int _renderOrder;
            [SerializeField] bool _isEnabledUVAdjust;
            public bool HasPropertyFilterTexScale;
            public Vector2 FilterTexScaleBase;
            public Material Material=>_movieMaterial;
        }
    }
}
