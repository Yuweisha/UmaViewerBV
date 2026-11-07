using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimateToUnity
{
    [Serializable]
    public partial class AnMeshParameter : AnScriptableObject
    {
        public List<AnCustomMeshInfoParameter> _customMeshInfoParameterList;
        public List<AnMeshInfoParameterGroup> _meshParameterGroupList;
    }
}
