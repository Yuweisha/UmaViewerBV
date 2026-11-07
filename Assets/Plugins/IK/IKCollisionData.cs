using System;
using UnityEngine;

namespace IK
{
    public class IKCollisionData : ScriptableObject
    {
        public Parameter[] ParameterArray;
        public bool IsHeightScale;
        public struct RuntimeParameter
        {
            public Parameter.CollisionType Type;
            public Vector3 Offset;
            public bool IsBodyScale;
            public float Param1;

            public float Radius => Param1;

            // Original 0x1fef460 copies the flag supplied by the asset owner;
            // the caller, not this struct, applies the selected scale.
            public static void InitializeFromParameter(ref RuntimeParameter runtimeParam, Parameter param, bool isBodyScale)
            {
                runtimeParam.Type = param.Type;
                runtimeParam.Offset = param.Offset;
                runtimeParam.IsBodyScale = isBodyScale;
                runtimeParam.Param1 = param.Param1;
            }
        }

        [Serializable]
        public class Parameter
        {
            public CollisionType Type;
            public string LinkBoneName;
            public Vector3 Offset;
            public Scene SceneType;
            public float Param1;

            public enum CollisionType
            {
                Sphere = 0
            }

            public enum Scene
            {
                Event = 1,
                Live = 2,
                All = 65535
            }
        }
    }

    
}



