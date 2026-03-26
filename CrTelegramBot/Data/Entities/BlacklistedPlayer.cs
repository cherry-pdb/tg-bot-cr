namespace CrTelegramBot.Data.Entities;

public sealed class BlacklistedPlayer
{
    public int Id { get; set; }
    public string PlayerTag { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}