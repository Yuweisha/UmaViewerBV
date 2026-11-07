using System.Globalization;
using Gallop.Live.Cutt;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gallop.Live
{
    public partial class StageController
    {
        private Mesh _depthOnlyCubeMesh;
        private Material _depthOnlyCubeMaterial;
        private Transform[] _depthOnlyCubeTransforms;
        private LiveTimelineData StageTimelineData => _boundTimelineControl != null
            ? _boundTimelineControl.data : Director.instance?._liveTimelineControl?.data;
        private bool IsProjectorStageEnabled => LiveQualitySettings.IsProjector ||
            (StageTimelineData?.ProjectorSettings?.IsEnabledProjectorObjectForLightMode ?? false);

        private void InitializeDepthOnlyStageObjects(LiveTimelineData data)
        {
            if (_depthOnlyCubeTransforms != null || data == null || !data.UseDepthOnlyCube || data.DepthOnlyCubeCount <= 0)
                return;

            // Original StaticVariableDefine.Live.StageController .cctor 0x5a0e130.
            // The 36 indices match the metadata initializer SHA256 0C115AFB...FEC04.
            _depthOnlyCubeMesh = new Mesh { name = "LiveDepthOnlyCube" };
            _depthOnlyCubeMesh.vertices = new[]
            {
                new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f),
                new Vector3(.5f,-.5f,.5f), new Vector3(-.5f,-.5f,.5f),
                new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(.5f,-.5f,-.5f), new Vector3(-.5f,-.5f,-.5f)
            };
            _depthOnlyCubeMesh.SetIndices(new[]
            {
                0,1,3,3,2,0,1,5,7,7,3,1,5,4,6,6,7,5,
                4,0,2,2,6,4,4,5,1,1,0,4,2,3,7,7,6,2
            }, MeshTopology.Triangles, 0);
            _depthOnlyCubeMesh.MarkDynamic();
            _depthOnlyCubeMesh.RecalculateNormals();
            _depthOnlyCubeMesh.RecalculateBounds();
            _depthOnlyCubeMesh.UploadMeshData(true);
            _depthOnlyCubeMaterial = new Material(ShaderManager.GetShader(ShaderManager.ShaderKinds.StageDepthOnly));
            _depthOnlyCubeTransforms = new Transform[data.DepthOnlyCubeCount];
            for (int i = 0; i < _depthOnlyCubeTransforms.Length; i++)
            {
                // Native CreateDepthOnlyCube formats i+1, not the array index.
                var cube = new GameObject("DepthOnlyCube" + (i + 1).ToString(CultureInfo.InvariantCulture));
                cube.transform.SetParent(transform);
                cube.AddComponent<MeshFilter>().sharedMesh = _depthOnlyCubeMesh;
                var renderer = cube.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _depthOnlyCubeMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _depthOnlyCubeTransforms[i] = cube.transform;
                RegisterObjectTransform(cube.transform, string.Empty);
            }
        }

        private void ReleaseDepthOnlyStageResources()
        {
            if (_depthOnlyCubeMesh != null) Destroy(_depthOnlyCubeMesh);
            if (_depthOnlyCubeMaterial != null) Destroy(_depthOnlyCubeMaterial);
            _depthOnlyCubeMesh = null;
            _depthOnlyCubeMaterial = null;
            _depthOnlyCubeTransforms = null;
        }
    }
}
