namespace CrTelegramBot.Data.Entities;

public sealed class UserLink
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    public string TelegramUsername { get; set; } = string.Empty;
    public string TelegramDisplayName { get; set; } = string.Empty;
    public string PlayerTag { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}