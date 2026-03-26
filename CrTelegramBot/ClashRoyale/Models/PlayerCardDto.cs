namespace CrTelegramBot.ClashRoyale.Models;

public sealed class PlayerCardDto
{
    public int Id { get; set; }
    public string? Rarity { get; set; }
    public int Count { get; set; }
    public int Level { get; set; }
    public int MaxLevel { get; set; }
    public int StarLevel { get; set; }
    public int EvolutionLevel { get; set; }
    public bool Used { get; set; }
    public JsonLocalizedNameDto? Name { get; set; }
    public int ElixirCost { get; set; }
    public int MaxEvolutionLevel { get; set; }
}

