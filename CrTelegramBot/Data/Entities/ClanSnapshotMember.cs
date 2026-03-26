namespace CrTelegramBot.Data.Entities;

public sealed class ClanSnapshotMember
{
    public int Id { get; set; }
    public string PlayerTag { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}