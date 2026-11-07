using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Samples evaluated bone/face poses after character physics, then writes GLB channels.</summary>
[DefaultExecutionOrder(30000)]
public sealed class GlbAnimationRecorder : MonoBehaviour
{
    private GlbCharacterExporter.Document _document;
    private string _path;
    private float _start;
    private readonly List<float> _times = new List<float>();
    private readonly List<List<float>> _positions = new List<List<float>>();
    private readonly List<List<float>> _rotations = new List<List<float>>();
    private readonly List<List<float>> _scales = new List<List<float>>();
    private readonly List<Quaternion> _previousRotations = new List<Quaternion>();
    private Matrix4x4 _initialRootInverse;
    private bool _physicsBaked;
    private Camera _camera;
    private readonly JArray _cameraFrames = new JArray();
    public bool IsRecording => _document != null;

    public void Begin(UmaContainerCharacter character, string path)
    {
        if (IsRecording) throw new InvalidOperationException("Already recording GLB");
        _document = GlbCharacterExporter.Capture(character);
        Initialize(character, path, null);
    }

    /// <summary>Record new motion against an existing, unchanged model payload.</summary>
    public void BeginReusingModel(UmaContainerCharacter character, string path, string modelPath, Camera camera = null)
    {
        if (IsRecording) throw new InvalidOperationException("Already recording GLB");
        var document = new GlbCharacterExporter.Document();
        try
        {
            using (var reader = new BinaryReader(File.OpenRead(modelPath)))
            {
                if (reader.ReadUInt32() != 0x46546C67u || reader.ReadUInt32() != 2u)
                    throw new InvalidDataException("Expected GLB version 2");
                uint totalLength = reader.ReadUInt32();
                int jsonLength = reader.ReadInt32();
                if (reader.ReadUInt32() != 0x4E4F534Au) throw new InvalidDataException("Missing JSON chunk");
                var json = JObject.Parse(Encoding.UTF8.GetString(reader.ReadBytes(jsonLength)));
                foreach (var property in json.Properties()) document.Json.Add(property.Name, property.Value.DeepClone());
                int binLength = reader.ReadInt32();
                if (reader.ReadUInt32() != 0x004E4942u || reader.BaseStream.Length != totalLength)
                    throw new InvalidDataException("Invalid BIN chunk");
                byte[] binary = reader.ReadBytes(binLength);
                if (binary.Length != binLength) throw new InvalidDataException("Truncated BIN chunk");
                document.Binary.Write(binary, 0, binary.Length);
            }
            document.Nodes = (JArray)document.Json["nodes"];
            document.Views = (JArray)document.Json["bufferViews"];
            document.Accessors = (JArray)document.Json["accessors"];
            // Reconstruct the exact source hierarchy, including duplicate bone
            // names under different heads/effects. Never match names globally.
            var parents = new Dictionary<int,int>();
            for (int i = 0; i < document.Nodes.Count; i++)
                foreach (int child in document.Nodes[i]["children"] ?? new JArray()) parents.Add(child, i);
            var sourceTransforms = character.GetComponentsInChildren<Transform>(true)
                .Where(t => t == character.transform || t.GetComponentsInParent<UIHandle>(true).Length == 0).ToArray();
            for (int i = 0; i < sourceTransforms.Length; i++)
            {
                Transform t = sourceTransforms[i];
                if (i >= document.Nodes.Count || (string)document.Nodes[i]["name"] != t.name)
                    throw new InvalidDataException("Existing model hierarchy differs at node " + i + ": " + t.name);
                if (i > 0 && (!parents.TryGetValue(i, out int parent) || parent >= i || sourceTransforms[parent] != t.parent))
                    throw new InvalidDataException("Existing model parent differs at node " + i);
                document.Transforms.Add(t);
            }
            for (int i = sourceTransforms.Length; i < document.Nodes.Count; i++)
                if (document.Nodes[i]["mesh"] == null) throw new InvalidDataException("Unmatched existing model node " + i);
            document.Json.Remove("animations");
            _document = document;
            Initialize(character, path, camera);
        }
        catch { document.Binary.Dispose(); _document = null; throw; }
    }

    private void Initialize(UmaContainerCharacter character, string path, Camera camera)
    {
        _physicsBaked = character.EnablePhysics;
        _camera = camera; _cameraFrames.Clear();
        _path = path; _start = Time.time; _initialRootInverse = character.transform.worldToLocalMatrix;
        _times.Clear(); _positions.Clear(); _rotations.Clear(); _scales.Clear(); _previousRotations.Clear();
        foreach (Transform t in _document.Transforms)
        {
            _positions.Add(new List<float>()); _rotations.Add(new List<float>()); _scales.Add(new List<float>());
            _previousRotations.Add(Quaternion.identity);
        }
    }

    private void LateUpdate()
    {
        if (!IsRecording) return;
        float time = Time.time - _start;
        if (_times.Count > 0 && time <= _times[_times.Count-1]) return;
        if (_document.Transforms.Any(t => t == null)) { Cancel(); return; }
        _times.Add(time);
        if (_camera != null)
        {
            Matrix4x4 relative = _initialRootInverse * _camera.transform.localToWorldMatrix;
            Vector3 p = relative.GetColumn(3);
            Quaternion q = relative.rotation;
            _cameraFrames.Add(new JObject { ["time"] = time,
                ["positionUnity"] = new JArray(p.x,p.y,p.z),
                ["rotationUnity"] = new JArray(q.x,q.y,q.z,q.w),
                ["verticalFovDegrees"] = _camera.fieldOfView,
                ["near"] = _camera.nearClipPlane, ["far"] = _camera.farClipPlane });
        }
        for (int i = 0; i < _document.Transforms.Count; i++)
        {
            Transform t = _document.Transforms[i];
            Vector3 p = t.localPosition, s = t.localScale; Quaternion q = t.localRotation;
            if (i == 0)
            {
                // Container motion is relative to the export origin; avoid baking stage offsets.
                Matrix4x4 root = _initialRootInverse * t.localToWorldMatrix;
                p = root.GetColumn(3); q = root.rotation; s = root.lossyScale;
            }
            p = GlbCharacterExporter.ToGltf(p); q = GlbCharacterExporter.ToGltf(q);
            if (_times.Count > 1 && Quaternion.Dot(q, _previousRotations[i]) < 0) q = new Quaternion(-q.x,-q.y,-q.z,-q.w);
            _previousRotations[i] = q;
            _positions[i].AddRange(new[] { p.x,p.y,p.z });
            _rotations[i].AddRange(new[] { q.x,q.y,q.z,q.w });
            _scales[i].AddRange(new[] { s.x,s.y,s.z });
        }
    }

    public void Finish()
    {
        if (!IsRecording) return;
        try
        {
            if (_times.Count == 0) throw new InvalidOperationException("No frames recorded");
            float offset = _times[0];
            int time = _document.AddFloats(_times.Select(t => t-offset).ToArray(), "SCALAR", 1, true);
            var samplers = new JArray(); var channels = new JArray();
            for (int i = 0; i < _document.Transforms.Count; i++)
            {
                AddChannel(i, "translation", _positions[i], "VEC3", 3, time, samplers, channels);
                AddChannel(i, "rotation", _rotations[i], "VEC4", 4, time, samplers, channels);
                AddChannel(i, "scale", _scales[i], "VEC3", 3, time, samplers, channels);
            }
            _document.Json["animations"] = new JArray(new JObject { ["name"] = _physicsBaked ? "Evaluated animation and physics" : "Evaluated animation",
                ["samplers"] = samplers, ["channels"] = channels,
                ["extras"] = new JObject { ["sampling"] = "rendered frames; variable timestamps", ["physicsBaked"] = _physicsBaked,
                    ["facialRepresentation"] = "Native bone channels; editable shape keys remain zero to avoid double deformation" } });
            _document.Write(_path);
            if (_cameraFrames.Count > 0)
            {
                foreach (JObject frame in _cameraFrames) frame["time"] = (float)frame["time"] - offset;
                File.WriteAllText(Path.ChangeExtension(_path, ".camera.json"), new JObject {
                    ["coordinates"] = "Unity camera relative to initial character origin; +Z forward, +Y up",
                    ["frames"] = _cameraFrames
                }.ToString());
            }
        }
        finally { Cancel(); }
    }

    private void AddChannel(int node, string path, List<float> values, string type, int components, int time, JArray samplers, JArray channels)
    {
        int sampler = samplers.Count;
        samplers.Add(new JObject { ["input"] = time, ["output"] = _document.AddFloats(values.ToArray(), type, components), ["interpolation"] = "LINEAR" });
        channels.Add(new JObject { ["sampler"] = sampler, ["target"] = new JObject { ["node"] = node, ["path"] = path } });
    }

    public void Cancel() { _document?.Binary.Dispose(); _document = null; _camera = null; }
    private void OnDestroy() { Cancel(); }
}
