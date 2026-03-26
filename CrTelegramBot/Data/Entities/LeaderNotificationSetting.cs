namespace CrTelegramBot.Data.Entities;

public sealed class LeaderNotificationSetting
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    public bool IsEnabled { get; set; }
}