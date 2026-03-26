using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrTelegramBot.ClashRoyale.Models;

public sealed class JsonLocalizedNameDtoConverter : JsonConverter<JsonLocalizedNameDto>
{
    public override JsonLocalizedNameDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            return new JsonLocalizedNameDto { Name = reader.GetString() };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Unexpected token {reader.TokenType} for JsonLocalizedNameDto");

        string? name = null;
        string? rawName = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var prop = reader.GetString();
            if (!reader.Read()) break;

            switch (prop)
            {
                case "name":
                    name = reader.TokenType == JsonTokenType.String ? reader.GetString() : name;
                    break;
                case "rawName":
                    rawName = reader.TokenType == JsonTokenType.String ? reader.GetString() : rawName;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new JsonLocalizedNameDto { Name = name, RawName = rawName };
    }

    public override void Write(Utf8JsonWriter writer, JsonLocalizedNameDto value, JsonSerializerOptions options)
    {
        // Для наших целей достаточно писать строкой.
        if (!string.IsNullOrWhiteSpace(value.Name))
        {
            writer.WriteStringValue(value.Name);
            return;
        }

        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(value.RawName))
            writer.WriteString("rawName", value.RawName);
        writer.WriteEndObject();
    }
}

