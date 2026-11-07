using System;
using Newtonsoft.Json;

namespace Gallop.Live.Cutt
{
    // Corpus PPtr objects (m_FileID/m_PathID) cannot be resolved to Unity assets
    // during JSON binding. Returning null matches the unresolved reference and
    // avoids Json.NET trying to construct engine objects without a default ctor.
    public sealed class UnityPptrNullConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => true;
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            reader.Skip();
            return null;
        }
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteNull();
    }
}
