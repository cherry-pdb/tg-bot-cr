namespace CrTelegramBot.Data.Entities;

public sealed class ChatUser
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    public string TelegramUsername { get; set; } = string.Empty;
    public string TelegramDisplayName { get; set; } = string.Empty;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}

