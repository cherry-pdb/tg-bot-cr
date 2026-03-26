namespace CrTelegramBot.Telegram;

public sealed record ParsedCommand(
    ParsedCommandKind Kind,
    string? PlayerTag = null,
    string? TelegramUsername = null,
    int? TopLimit = null);