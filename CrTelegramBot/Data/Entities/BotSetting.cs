namespace CrTelegramBot.Data.Entities;

public sealed class BotSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}