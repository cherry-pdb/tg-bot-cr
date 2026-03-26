using System.Text.Json.Serialization;

namespace CrTelegramBot.ClashRoyale.Models;

[JsonConverter(typeof(JsonLocalizedNameDtoConverter))]
public sealed class JsonLocalizedNameDto
{
    public string? Name { get; set; }
    public string? RawName { get; set; }
}

