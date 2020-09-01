using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Json.RestConverter
{
    public class RestConverter<T> : JsonConverter<T> where T : class, new()
    {
        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var restProperty = GetRestPropertyForType(typeof(T));

            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Dictionary must be JSON object.");

            var deserializedObject = DeserializeFromReader(ref reader, options, restProperty);
            var typeProperties = typeToConvert.GetProperties();
            while (true)
            {
                if (!reader.Read())
                    throw new JsonException("Incomplete JSON object");

                if (reader.TokenType == JsonTokenType.EndObject)
                    // at the end of the object, return
                    return deserializedObject;

                // This is the property name (json key)
                var key = reader.GetString();

                // Filter out any existing properties that were already potentially deserialized
                // Start with the obvious and less expensive checks first
                if (typeProperties.Any(p =>
                    String.CompareOrdinal(key, p.Name) == 0 ||
                    String.CompareOrdinal(key, $"{p.Name.Substring(0, 1).ToLower()}{p.Name.Substring(1)}") == 0 ||
                    String.CompareOrdinal(key,
                        (p.GetCustomAttributes(false)
                                .FirstOrDefault(c => c is JsonPropertyNameAttribute) as
                            JsonPropertyNameAttribute)?.Name) == 0))
                {
                    // If found, skip this property
                    reader.Read();
                    continue;
                }

                if (!reader.Read())
                    throw new JsonException("Incomplete JSON object");

                if (reader.TokenType == JsonTokenType.EndObject)
                    // at the end of the object, return
                    return deserializedObject;

                // Get the value from the JSON and set it to the rest properties dictionary 
                var valueForKey = JsonSerializer.Deserialize<object>(ref reader, options)?.ToString();
                ((Dictionary<string, string>) restProperty.GetValue(deserializedObject))[key] = valueForKey;
            }
        }

        public override void Write(
            Utf8JsonWriter writer,
            T value,
            JsonSerializerOptions options) =>
            throw new NotSupportedException($"Writing {typeof(T).Name} is not supported");
        
        private JsonSerializerOptions CloneOptions(JsonSerializerOptions original)
        {
            var cloned = new JsonSerializerOptions();

            foreach (var converter in original.Converters.Except(
                original.Converters.Where(c => c.GetType() == this.GetType())))
            {
                cloned.Converters.Add(converter);
            }

            return cloned;
        }

        private PropertyInfo GetRestPropertyForType(Type type)
        {
            var restProperties = type.GetProperties()
                .Where(p => p.GetCustomAttribute(typeof(JsonRestPropertyAttribute)) != null);

            if (!restProperties.Any())
                throw new NotSupportedException(
                    $"Expected at least 1 property that is annotated with a {nameof(JsonRestPropertyAttribute)}");
            if (restProperties.Count() > 1)
                throw new NotSupportedException(
                    $"Multiple rest properties are not supported, there can be only 1 {nameof(JsonRestPropertyAttribute)}");

            var restProperty = restProperties.First();
            var typeOfProperty = restProperty.PropertyType;

            if (typeOfProperty != typeof(Dictionary<string, string>))
                throw new NotSupportedException(
                    $"The rest property {restProperty.Name} must be of type {typeof(Dictionary<string, string>)}. Other types are not supporter ({typeOfProperty.Name})");

            if (!restProperty.CanWrite || !restProperty.GetSetMethod( /*nonPublic*/ true).IsPublic)
                throw new NotSupportedException(
                    $"The rest property {restProperty.Name} requires a settable Set method.");

            return restProperty;
        }

        private T DeserializeFromReader(ref Utf8JsonReader reader, JsonSerializerOptions options,
            PropertyInfo restProperty)
        {
            // Copy the reader so that we do not affect the original reader
            // (Utf8JsonReader is a struct)
            var readerCopy = reader;
            var jsonDocument = JsonDocument.ParseValue(ref readerCopy);
            var rawJson = jsonDocument.RootElement.GetRawText();
            var newOptions = CloneOptions(options);
            // Deserialize using default JSON deserialization
            var deserializedObject = JsonSerializer.Deserialize<T>(rawJson, newOptions);
            // Initialize properties dictionary for the rest properties
            restProperty.SetValue(deserializedObject, new Dictionary<string, string>());
            return deserializedObject;
        }
    }
}