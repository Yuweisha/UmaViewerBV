using System.Collections.Generic;
using UnityEngine;

namespace Gallop.RenderPipeline
{
    // Original LensFlareManager.AddCamera 0x51cd470 stores the FlareLayer
    // component once. Feature enqueue alone does not register a camera.
    public static class NativeLensFlareCameraRegistry
    {
        static readonly Dictionary<Camera, FlareLayer> cameras = new Dictionary<Camera, FlareLayer>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => cameras.Clear();

        public static void AddCamera(Camera camera)
        {
            if (camera == null || !camera.TryGetComponent<FlareLayer>(out var layer)) return;
            if (!cameras.ContainsKey(camera)) cameras.Add(camera, layer);
        }

        public static void RemoveCamera(Camera camera)
        {
            if (!ReferenceEquals(camera, null)) cameras.Remove(camera);
        }

        public static bool IsRegistered(Camera camera) => camera != null && cameras.ContainsKey(camera);

        // 0x51cf4d0 / 0x51cee90: disabled layers skip both update and draw.
        // Neither gate removes the registration or clears visibility history.
        public static bool ShouldExecute(Camera camera)
        {
            if (camera == null || !cameras.TryGetValue(camera, out var layer)) return false;
            return layer.enabled;
        }

        // Resource adaptation only. Call at owners whose original serialized
        // prefab contains FlareLayer, or the original explicit AddComponent site.
        // Existing component state is preserved, including enabled=false.
        public static void RestoreSerializedFlareLayer(Camera camera)
        {
            if (camera == null) return;
            if (!camera.TryGetComponent<FlareLayer>(out _)) camera.gameObject.AddComponent<FlareLayer>();
            // Viewer scene cameras can acquire CameraData before Director binds
            // the original camera resource. Correct that installation ordering.
            if (camera.GetComponent<CameraData>() != null) AddCamera(camera);
        }
    }
}
