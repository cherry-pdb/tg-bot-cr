namespace CrTelegramBot.ClashRoyale.Models;

public sealed class ArenaDto
{
    public JsonLocalizedNameDto? Name { get; set; }
    public int Id { get; set; }
    public string? RawName { get; set; }
}