using UnityEngine;
using UnityEngine.Rendering;

// Original global script identity. The beam shader consumes these tangents,
// not the imported tangent basis (2.32.0 ConvertBeamLightMesh@26857f0).
public sealed class BgBeamLight : MonoBehaviour
{
    [SerializeField] float cameraDistance = 15f;
    Mesh _mesh;

    void Start()
    {
        var filter = GetComponent<MeshFilter>();
        _mesh = ConvertBeamLightMesh(filter.sharedMesh);
        if (_mesh != null) filter.sharedMesh = _mesh;
    }

    Mesh ConvertBeamLightMesh(Mesh source)
    {
        if (!source.isReadable) return null;
        var vertices = source.vertices;
        var tangents = new Vector4[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            tangents[i] = new Vector4(-vertices[i].x, cameraDistance, -vertices[i].z, 1f);
        return new Mesh
        {
            vertices = vertices, triangles = source.triangles,
            uv = source.uv, uv2 = source.uv2, uv3 = source.uv3,
            normals = source.normals, tangents = tangents
        };
    }

    void OnDestroy() { CoreUtils.Destroy(_mesh); _mesh = null; }
}
