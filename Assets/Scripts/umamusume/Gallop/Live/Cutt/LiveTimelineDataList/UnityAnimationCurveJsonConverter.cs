using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Corpus curves use Unity's serialized layout: m_Curve / m_PreInfinity /
    // m_PostInfinity / m_RotationOrder. Json.NET has no default binding for it.
    public sealed class UnityAnimationCurveJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(AnimationCurve);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var json = JObject.Load(reader);
            var keysJson = json["m_Curve"] as JArray ?? new JArray();
            var keys = new Keyframe[keysJson.Count];
            for (int i = 0; i < keysJson.Count; i++)
            {
                var k = keysJson[i];
                var key = new Keyframe(
                    k["time"] != null ? (float)k["time"] : 0f,
                    k["value"] != null ? (float)k["value"] : 0f,
                    k["inSlope"] != null ? (float)k["inSlope"] : 0f,
                    k["outSlope"] != null ? (float)k["outSlope"] : 0f);
                if (k["weightedMode"] != null) key.weightedMode = (WeightedMode)(int)k["weightedMode"];
                if (k["inWeight"] != null) key.inWeight = (float)k["inWeight"];
                if (k["outWeight"] != null) key.outWeight = (float)k["outWeight"];
                keys[i] = key;
            }
            var curve = new AnimationCurve { keys = keys };
            if (json["m_PreInfinity"] != null) curve.preWrapMode = (WrapMode)(int)json["m_PreInfinity"];
            if (json["m_PostInfinity"] != null) curve.postWrapMode = (WrapMode)(int)json["m_PostInfinity"];
            return curve;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var curve = (AnimationCurve)value;
            var json = new JObject
            {
                ["m_Curve"] = new JArray(Array.ConvertAll(curve.keys, key => new JObject
                {
                    ["time"] = key.time,
                    ["value"] = key.value,
                    ["inSlope"] = key.inTangent,
                    ["outSlope"] = key.outTangent,
                    ["weightedMode"] = (int)key.weightedMode,
                    ["inWeight"] = key.inWeight,
                    ["outWeight"] = key.outWeight
                })),
                ["m_PreInfinity"] = (int)curve.preWrapMode,
                ["m_PostInfinity"] = (int)curve.postWrapMode,
                ["m_RotationOrder"] = 4
            };
            json.WriteTo(writer);
        }
    }
}
