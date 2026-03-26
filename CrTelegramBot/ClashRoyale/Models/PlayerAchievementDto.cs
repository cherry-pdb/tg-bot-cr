namespace CrTelegramBot.ClashRoyale.Models;

public sealed class PlayerAchievementDto
{
    public JsonLocalizedNameDto? Name { get; set; }
    public int Value { get; set; }
    public int Target { get; set; }
    public int Stars { get; set; }
    public JsonLocalizedNameDto? Info { get; set; }
    public JsonLocalizedNameDto? CompletionInfo { get; set; }
}

