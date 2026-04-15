namespace CrTelegramBot.Configuration;

public sealed class BotConfig
{
    public const string SectionName = "BotConfig";

    public string TelegramBotToken { get; init; } = string.Empty;
    public string ClashRoyaleApiToken { get; init; } = string.Empty;
    public const string MainChatIdSettingKey = "main_chat_id";

    public long MainChatId { get; set; }
    public string ClanTag { get; init; } = string.Empty;
    public int ReminderHourUtc { get; init; } = 8;
    public string? WarStartLocalTime { get; init; }
    public string[]? WarStartDaysOfWeek { get; init; }
    public int WarEndHourUtc { get; init; } = 23;
    public string? WarEndLocalTime { get; init; }
    public int WarEndDaySummaryMinutesBeforeEnd { get; init; } = 1;
    public static string WarEndDaySummaryTimeZoneId => "Russian Standard Time";
    public string? WarEndDaySummaryLocalTime { get; init; }
    public string[]? WarDaysOfWeek { get; init; }
    public int[] WarNudgesHoursBeforeEnd { get; init; } = [4, 2];
    public int TopLocationId { get; init; } = 57000193;
    public string[] LeaderUsernames { get; init; } = [];
    public int ClanMonitorIntervalSeconds { get; init; } = 60;
}