using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gallop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Blender interchange with native bone names, meters and a neutral reference pose.</summary>
public static class GlbCharacterExporter
{
    public sealed class MorphBinding
    {
        public int Node;
        public FacialMorph[] Morphs;
    }

    public sealed class Document
    {
        public readonly JObject Json = new JObject();
        public readonly MemoryStream Binary = new MemoryStream();
        public readonly List<Transform> Transforms = new List<Transform>();
        public readonly List<MorphBinding> MorphBindings = new List<MorphBinding>();
        internal JArray Nodes = new JArray(), Views = new JArray(), Accessors = new JArray();

        public int AddFloats(float[] values, string type, int components, bool bounds = false)
        {
            var bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; ++i)
            {
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i])) throw new InvalidDataException("Nonfinite glTF value");
                Buffer.BlockCopy(BitConverter.GetBytes(values[i]), 0, bytes, i * 4, 4);
            }
            var accessor = new JObject { ["bufferView"] = AddBytes(bytes), ["componentType"] = 5126,
                ["count"] = values.Length / components, ["type"] = type };
            if (bounds)
            {
                float[] min = Enumerable.Repeat(float.PositiveInfinity, components).ToArray();
                float[] max = Enumerable.Repeat(float.NegativeInfinity, components).ToArray();
                for (int i = 0; i < values.Length; i++) { int c = i % components; min[c] = Mathf.Min(min[c], values[i]); max[c] = Mathf.Max(max[c], values[i]); }
                accessor["min"] = new JArray(min); accessor["max"] = new JArray(max);
            }
            int index = Accessors.Count; Accessors.Add(accessor); return index;
        }

        public int AddBytes(byte[] bytes)
        {
            while ((Binary.Position & 3) != 0) Binary.WriteByte(0);
            int index = Views.Count;
            Views.Add(new JObject { ["buffer"] = 0, ["byteOffset"] = Binary.Position, ["byteLength"] = bytes.Length });
            Binary.Write(bytes, 0, bytes.Length); return index;
        }

        internal void SetTarget(int accessor, int target)
        {
            int view = (int)Accessors[accessor]["bufferView"];
            Views[view]["target"] = target;
        }

        internal int AddIntegers(int[] values, string type, int components, bool ushortValues)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                foreach (int value in values)
                {
                    if (value < 0 || (ushortValues && value > ushort.MaxValue)) throw new InvalidDataException("Invalid glTF index");
                    if (ushortValues) writer.Write((ushort)value); else writer.Write((uint)value);
                }
                int index = Accessors.Count;
                Accessors.Add(new JObject { ["bufferView"] = AddBytes(stream.ToArray()), ["componentType"] = ushortValues ? 5123 : 5125,
                    ["count"] = values.Length / components, ["type"] = type });
                return index;
            }
        }

        public void Write(string path)
        {
            Json["nodes"] = Nodes; Json["bufferViews"] = Views; Json["accessors"] = Accessors;
            Json["buffers"] = new JArray(new JObject { ["byteLength"] = Binary.Length });
            byte[] json = Encoding.UTF8.GetBytes(Json.ToString(Formatting.None)), binary = Binary.ToArray();
            int jsonLength = (json.Length + 3) & ~3, binLength = (binary.Length + 3) & ~3;
            string fullPath = Path.GetFullPath(path), temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            try
            {
                using (var file = File.Create(temporary))
                using (var writer = new BinaryWriter(file))
                {
                    writer.Write(0x46546C67u); writer.Write(2u); writer.Write((uint)(12 + 8 + jsonLength + 8 + binLength));
                    writer.Write((uint)jsonLength); writer.Write(0x4E4F534Au); writer.Write(json);
                    for (int i = json.Length; i < jsonLength; i++) writer.Write((byte)32);
                    writer.Write((uint)binLength); writer.Write(0x004E4942u); writer.Write(binary);
                    for (int i = binary.Length; i < binLength; i++) writer.Write((byte)0);
                }
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null); else File.Move(temporary, fullPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static void Export(UmaContainerCharacter character, string path)
    {
        Document document = Capture(character);
        try { document.Write(path); }
        finally { document.Binary.Dispose(); }
    }

    // A non-singular exchange reference preserves hidden alternate geometry.
    // The native driven-key package restores authored tiny scales as pose data.
    public static Vector3 NativeFaceReferenceScale(Vector3 s) => new Vector3(
        Mathf.Abs(s.x)<1e-8f ? 1 : s.x, Mathf.Abs(s.y)<1e-8f ? 1 : s.y, Mathf.Abs(s.z)<1e-8f ? 1 : s.z);

    public static Document Capture(UmaContainerCharacter character, bool nativeFace = false)
    {
        if (character == null) throw new ArgumentNullException(nameof(character));
        var document = new Document();
        try
        {
            using (var snapshot = new PMXExportStateSnapshot(character))
            {
                snapshot.FreezeForExport();
                character.ResetBodyPose();
                character.FaceOverrideData = null;
                if (nativeFace)
                {
                    character.FaceDrivenKeyTarget?.ClearAllWeights();
                    character.FaceDrivenKeyTarget?.ChangeMorph();
                    foreach (var t in character.GetComponentsInChildren<Transform>(true))
                        t.localScale = NativeFaceReferenceScale(t.localScale);
                }
                else character.FaceDrivenKeyTarget?.FacialResetAll();
                Build(character, document, nativeFace);
            }
            return document;
        }
        catch { document.Binary.Dispose(); throw; }
    }

    public static Vector3 ToGltf(Vector3 v) => new Vector3(v.x, v.y, -v.z);
    public static Quaternion ToGltf(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);
    private static JArray Vec(Vector3 v) => new JArray(v.x, v.y, v.z);
    private static float[] Vectors(Vector3[] values) => values.SelectMany(v => new[] { v.x, v.y, v.z }).ToArray();

    private static void Build(UmaContainerCharacter character, Document doc, bool nativeFace)
    {
        doc.Json["asset"] = new JObject { ["version"] = "2.0", ["generator"] = "UmaViewer unified",
            ["extras"] = new JObject { ["referencePose"] = nativeFace ? "non-singular native face exchange reference" : "native initialization", ["units"] = "meters",
                ["physics"] = "Baked animation only; CySpring solver is not a glTF extension",
                ["materialLimit"] = "Unlit base texture approximation; game shader textures retained as extras" } };
        doc.Json["scene"] = 0; doc.Json["scenes"] = new JArray(new JObject { ["nodes"] = new JArray(0) });
        var map = new Dictionary<Transform,int>();
        foreach (Transform t in character.GetComponentsInChildren<Transform>(true))
        {
            // Handles/poles are UI controls, not part of the imported model.
            if (t != character.transform && t.GetComponentsInParent<UIHandle>(true).Length > 0) continue;
            map[t] = doc.Transforms.Count; doc.Transforms.Add(t);
            Vector3 position = t == character.transform ? Vector3.zero : ToGltf(t.localPosition);
            Quaternion rotation = t == character.transform ? Quaternion.identity : ToGltf(t.localRotation);
            Vector3 scale = t == character.transform ? Vector3.one : t.localScale;
            doc.Nodes.Add(new JObject { ["name"] = t.name, ["translation"] = Vec(position),
                ["rotation"] = new JArray(rotation.x, rotation.y, rotation.z, rotation.w), ["scale"] = Vec(scale) });
        }
        foreach (var pair in map)
        {
            if (pair.Key == character.transform || !map.TryGetValue(pair.Key.parent, out int parent)) continue;
            if (doc.Nodes[parent]["children"] == null) doc.Nodes[parent]["children"] = new JArray();
            ((JArray)doc.Nodes[parent]["children"]).Add(pair.Value);
        }
        var meshes = new JArray(); var skins = new JArray(); var materials = new JArray();
        var images = new JArray(); var textures = new JArray();
        var materialMap = new Dictionary<Material,int>(); var textureMap = new Dictionary<Texture2D,int>();
        Matrix4x4 rootInverse = character.transform.worldToLocalMatrix;
        foreach (Renderer renderer in character.GetComponentsInChildren<Renderer>(false))
        {
            if (!renderer.enabled || !map.ContainsKey(renderer.transform)) continue;
            var skin = renderer as SkinnedMeshRenderer;
            Mesh source = skin != null ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (source == null || source.vertexCount == 0) continue;
            Mesh baked = null;
            try
            {
                if (skin != null) { baked = new Mesh(); skin.BakeMesh(baked, false); }
                Mesh mesh = baked != null ? baked : source;
                Matrix4x4 toRoot = rootInverse * renderer.transform.localToWorldMatrix;
                Vector3[] positions = mesh.vertices.Select(v => ToGltf(toRoot.MultiplyPoint3x4(v))).ToArray();
                Vector3[] normals = mesh.normals.Select(v => ToGltf(toRoot.inverse.transpose.MultiplyVector(v).normalized)).ToArray();
                // Some authored meshes contain collapsed/unused vertices with zero normals.
                // glTF requires unit normals even for those vertices. Fix only the export copy.
                if (normals.Length == positions.Length)
                    for (int i = 0; i < normals.Length; i++)
                        if (normals[i].sqrMagnitude < 0.5f) normals[i] = Vector3.up;
                int positionAccessor = doc.AddFloats(Vectors(positions), "VEC3", 3, true);
                var attributes = new JObject { ["POSITION"] = positionAccessor };
                if (normals.Length == positions.Length) attributes["NORMAL"] = doc.AddFloats(Vectors(normals), "VEC3", 3);
                Vector2[] uv = mesh.uv;
                if (uv.Length == positions.Length) attributes["TEXCOORD_0"] = doc.AddFloats(uv.SelectMany(v => new[] {v.x, 1-v.y}).ToArray(), "VEC2", 2);
                int skinIndex = -1;
                if (skin != null && skin.bones.Length > 0)
                {
                    if (skin.bones.Any(b => b == null || !map.ContainsKey(b))) throw new InvalidDataException("Skin contains unresolved bone");
                    BoneWeight[] weights = source.boneWeights;
                    if (weights.Length != positions.Length) throw new InvalidDataException("Skin weight count does not match vertices");
                    var joints = new List<int>(); var values = new List<float>();
                    foreach (BoneWeight w in weights)
                    {
                        int[] ids = {w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};
                        float[] ws = {w.weight0,w.weight1,w.weight2,w.weight3}; float sum = ws.Sum();
                        if (sum <= 0) throw new InvalidDataException("Vertex has no skin weight");
                        for (int i = 0; i < 4; i++)
                        {
                            if (ws[i] < 0 || (ws[i] > 0 && (ids[i] < 0 || ids[i] >= skin.bones.Length))) throw new InvalidDataException("Invalid skin weight/index");
                            joints.Add(ws[i] == 0 ? 0 : ids[i]); values.Add(ws[i]/sum);
                        }
                    }
                    attributes["JOINTS_0"] = doc.AddIntegers(joints.ToArray(), "VEC4", 4, true);
                    attributes["WEIGHTS_0"] = doc.AddFloats(values.ToArray(), "VEC4", 4);
                    var matrices = new List<float>(); var flip = Matrix4x4.Scale(new Vector3(1,1,-1));
                    foreach (Transform bone in skin.bones)
                    {
                        Matrix4x4 matrix = flip * (rootInverse * bone.localToWorldMatrix).inverse * flip;
                        for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) matrices.Add(matrix[r,c]);
                    }
                    skinIndex = skins.Count;
                    skins.Add(new JObject { ["joints"] = new JArray(skin.bones.Select(b => map[b])),
                        ["inverseBindMatrices"] = doc.AddFloats(matrices.ToArray(), "MAT4", 16) });
                }
                var targets = new JArray(); var targetNames = new JArray();
                FacialMorph[] facialMorphs = nativeFace ? new FacialMorph[0] : GetFacialMorphs(character, renderer.name);
                if (skin != null && character.FaceDrivenKeyTarget != null && facialMorphs.Length > 0)
                {
                    foreach (FacialMorph morph in facialMorphs)
                    {
                        character.FaceDrivenKeyTarget.ClearAllWeights(); morph.weight = 1;
                        character.FaceDrivenKeyTarget.ChangeMorph(); skin.BakeMesh(baked, false);
                        Vector3[] morphed = baked.vertices;
                        Vector3[] delta = new Vector3[morphed.Length];
                        for (int i = 0; i < delta.Length; i++) delta[i] = ToGltf(toRoot.MultiplyPoint3x4(morphed[i])) - positions[i];
                        targets.Add(new JObject { ["POSITION"] = doc.AddFloats(Vectors(delta), "VEC3", 3, true) });
                        doc.SetTarget((int)targets[targets.Count-1]["POSITION"], 34962);
                        targetNames.Add(morph.name);
                    }
                    character.FaceDrivenKeyTarget.ClearAllWeights(); character.FaceDrivenKeyTarget.ChangeMorph();
                }
                var primitives = new JArray(); Material[] assigned = renderer.sharedMaterials;
                foreach (JProperty attribute in attributes.Properties()) doc.SetTarget((int)attribute.Value,34962);
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    // Unity does not draw explicit null material slots. Exporting them
                    // without a glTF material creates an opaque white duplicate surface.
                    if (sub >= assigned.Length || assigned[sub] == null) continue;
                    if (source.GetTopology(sub) != MeshTopology.Triangles) continue;
                    int[] indices = source.GetTriangles(sub); if (indices.Length == 0) continue;
                    if (toRoot.determinant > 0) for (int i = 0; i < indices.Length; i+=3) { int temp = indices[i+1]; indices[i+1]=indices[i+2]; indices[i+2]=temp; }
                    var primitive = new JObject { ["attributes"] = attributes.DeepClone(), ["indices"] = doc.AddIntegers(indices,"SCALAR",1,false), ["mode"] = 4 };
                    doc.SetTarget((int)primitive["indices"],34963);
                    if (sub < assigned.Length && assigned[sub] != null)
                        primitive["material"] = AddMaterial(assigned[sub], doc, materials, images, textures, materialMap, textureMap);
                    if (targets.Count > 0) primitive["targets"] = targets.DeepClone();
                    primitives.Add(primitive);
                }
                if (primitives.Count == 0) continue;
                var meshJson = new JObject { ["name"] = renderer.name, ["primitives"] = primitives };
                if (targets.Count > 0) { meshJson["weights"] = new JArray(Enumerable.Repeat(0f, targets.Count)); meshJson["extras"] = new JObject { ["targetNames"] = targetNames }; }
                int nodeIndex = doc.Nodes.Count;
                var node = new JObject { ["name"] = renderer.name+"_Geometry", ["mesh"] = meshes.Count };
                if (skinIndex >= 0) node["skin"] = skinIndex;
                doc.Nodes.Add(node); meshes.Add(meshJson);
                if (skinIndex >= 0) ((JArray)doc.Json["scenes"][0]["nodes"]).Add(nodeIndex);
                else
                {
                    if (doc.Nodes[0]["children"] == null) doc.Nodes[0]["children"] = new JArray();
                    ((JArray)doc.Nodes[0]["children"]).Add(nodeIndex);
                }
                if (targets.Count > 0) doc.MorphBindings.Add(new MorphBinding { Node=nodeIndex, Morphs=facialMorphs });
            }
            finally { if (baked != null) UnityEngine.Object.DestroyImmediate(baked); }
        }
        if (meshes.Count == 0) throw new InvalidDataException("Character has no visible triangle meshes");
        doc.Json["meshes"] = meshes;
        if (skins.Count > 0) doc.Json["skins"] = skins;
        if (materials.Count > 0) doc.Json["materials"] = materials;
        if (images.Count > 0) { doc.Json["images"] = images; doc.Json["textures"] = textures; }
        doc.Json["extensionsUsed"] = new JArray("KHR_materials_unlit");
        if (materials.Any(m => m["pbrMetallicRoughness"]?["baseColorTexture"]?["extensions"]?["KHR_texture_transform"] != null))
            ((JArray)doc.Json["extensionsUsed"]).Add("KHR_texture_transform");
    }

    private static FacialMorph[] GetFacialMorphs(UmaContainerCharacter c, string name)
    {
        var face = c.FaceDrivenKeyTarget;
        if (face == null) return new FacialMorph[0];
        if (name == "M_Face") return face.EyeBrowMorphs.Concat(face.EyeMorphs).Concat(face.MouthMorphs).ToArray();
        if (name == "M_Mayu") return face.EyeBrowMorphs.ToArray();
        if (name == "M_Eye") return face.EyeMorphs.ToArray();
        if (name == "M_Mouth") return face.MouthMorphs.ToArray();
        if (name == "M_Hair") return face.EarMorphs.ToArray();
        return new FacialMorph[0];
    }

    private static int AddMaterial(Material material, Document doc, JArray materials, JArray images, JArray textures,
        Dictionary<Material,int> map, Dictionary<Texture2D,int> textureMap)
    {
        if (map.TryGetValue(material, out int existing)) return existing;
        var pbr = new JObject { ["metallicFactor"] = 0, ["roughnessFactor"] = 1, ["baseColorFactor"] = new JArray(1,1,1,1) };
        var props = new JObject();
        foreach (string property in material.GetTexturePropertyNames())
        {
            var texture = material.GetTexture(property) as Texture2D;
            if (texture == null) continue;
            if (!textureMap.TryGetValue(texture, out int index))
            {
                Texture2D readable = TextureExporter.duplicateTexture(texture);
                byte[] png;
                try { png = readable.EncodeToPNG(); }
                finally { UnityEngine.Object.DestroyImmediate(readable); }
                index = textures.Count; textureMap[texture] = index;
                images.Add(new JObject { ["name"] = texture.name, ["bufferView"] = doc.AddBytes(png), ["mimeType"] = "image/png" });
                textures.Add(new JObject { ["source"] = images.Count-1 });
            }
            props[property] = new JObject { ["texture"] = index, ["scale"] = new JArray(material.GetTextureScale(property).x,material.GetTextureScale(property).y),
                ["offset"] = new JArray(material.GetTextureOffset(property).x,material.GetTextureOffset(property).y) };
            if (property == "_MainTex" || property == "_BaseMap")
            {
                var info = new JObject { ["index"] = index };
                Vector2 scale = material.GetTextureScale(property), offset = material.GetTextureOffset(property);
                if (scale != Vector2.one || offset != Vector2.zero)
                    info["extensions"] = new JObject { ["KHR_texture_transform"] = new JObject {
                        ["scale"] = new JArray(scale.x, scale.y),
                        // Exported UVs are (u,1-v), so convert Unity's texture offset too.
                        ["offset"] = new JArray(offset.x, 1-scale.y-offset.y) } };
                pbr["baseColorTexture"] = info;
            }
        }
        var json = new JObject { ["name"] = material.name, ["pbrMetallicRoughness"] = pbr, ["doubleSided"] = true,
            ["extensions"] = new JObject { ["KHR_materials_unlit"] = new JObject() },
            ["extras"] = new JObject { ["unityShader"] = material.shader != null ? material.shader.name : "", ["unityTextures"] = props } };
        if (material.renderQueue >= 3000) json["alphaMode"] = "BLEND";
        else if (material.HasProperty("_Cutoff")) { json["alphaMode"] = "MASK"; json["alphaCutoff"] = Mathf.Clamp01(material.GetFloat("_Cutoff")); }
        int result = materials.Count; map[material] = result; materials.Add(json); return result;
    }
}
