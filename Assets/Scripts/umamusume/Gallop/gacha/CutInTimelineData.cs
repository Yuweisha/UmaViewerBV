using UnityEngine;

namespace Gallop.CutIn.Cutt
{
    public sealed class CutInTimelineData : ScriptableObject
    {
        public float _timeLength;
        public float maxForcalSize;
        public CutInTimelineWorkSheet[] _worksheetList;
        public Vector3 _backgroundPosOffset;
        public Vector3 _backgroundRotationOffset;
        public MonitorCameraSettings _monitorCameraSettings;

        [System.Serializable]
        public class MonitorCameraSettings
        {
            public bool IsUseCustomResolution, UseMSAA_MonitorCamera;
            public Vector2Int TextureCustomResolution;
        }
    }
}
