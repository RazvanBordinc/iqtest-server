using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IqTest_server.Converters
{
    public class AnswerValueJsonConverter : JsonConverter<object>
    {
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long longValue))
                        return longValue;
                    if (reader.TryGetDouble(out double doubleValue))
                        return doubleValue;
                    return reader.GetInt32();
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    // Handle complex objects as JsonElements
                    return JsonDocument.ParseValue(ref reader).RootElement.Clone();
                default:
                    throw new JsonException($"Unexpected token type: {reader.TokenType}");
            }
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
            }
            else
            {
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
            }
        }
    }
}