namespace CrTelegramBot.Configuration;

public sealed class BotConfig
{
    public const string SectionName = "BotConfig";

    public string TelegramBotToken { get; set; } = string.Empty;
    public string ClashRoyaleApiToken { get; set; } = string.Empty;
    public long MainChatId { get; set; }
    public string ClanTag { get; set; } = string.Empty;
    public int ReminderHourUtc { get; set; } = 8;
    public int WarEndHourUtc { get; set; } = 23;
    public int[] WarNudgesHoursBeforeEnd { get; set; } = [4, 2];
    public int TopLocationId { get; set; } = 57000193;
    public string[] LeaderUsernames { get; set; } = [];
    public int ClanMonitorIntervalSeconds { get; set; } = 60;
}