namespace CrTelegramBot.Telegram;

public sealed class CommandParser
{
    public ParsedCommand? Parse(string text)
    {
        text = text.Trim();

        if (text.StartsWith("Добавить руководителя", StringComparison.OrdinalIgnoreCase))
            return ParseOptionalUsername(text, "Добавить руководителя", ParsedCommandKind.AddLeader);

        if (text.StartsWith("Удалить руководителя", StringComparison.OrdinalIgnoreCase))
            return ParseOptionalUsername(text, "Удалить руководителя", ParsedCommandKind.RemoveLeader);

        if (text.Equals("Руководители", StringComparison.OrdinalIgnoreCase) || text.Equals("Список руководителей", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(ParsedCommandKind.Leaders);

        if (text.StartsWith("Подключить#", StringComparison.OrdinalIgnoreCase))
            return ParseTagAndUsername(text, "Подключить#", ParsedCommandKind.Connect);

        if (text.StartsWith("Отключить#", StringComparison.OrdinalIgnoreCase))
            return ParseTagAndUsername(text, "Отключить#", ParsedCommandKind.Disconnect);

        if (text.Equals("Включить уведомления", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(ParsedCommandKind.EnableNotifications);

        if (text.Equals("Отключить уведомления", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(ParsedCommandKind.DisableNotifications);

        if (text.Equals("Команды", StringComparison.OrdinalIgnoreCase) || text.Equals("Команда", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(ParsedCommandKind.Commands);

        if (text.Equals("Участники", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(ParsedCommandKind.Participants);

        if (text.Equals("Что с КВ", StringComparison.OrdinalIgnoreCase) || text.Equals("Что с КВ?", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(ParsedCommandKind.WarStatus);

        if (text.Equals("Напомни о КВ", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(ParsedCommandKind.RemindWar);

        if (text.StartsWith("В ЧС#", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(ParsedCommandKind.Blacklist, text[5..].Trim());

        if (text.StartsWith("В ЧС", StringComparison.OrdinalIgnoreCase))
            return ParseOptionalUsername(text, "В ЧС", ParsedCommandKind.Blacklist);

        if (text.StartsWith("Из ЧС#", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(ParsedCommandKind.RemoveBlacklist, text[6..].Trim());

        if (text.StartsWith("Из ЧС", StringComparison.OrdinalIgnoreCase))
            return ParseOptionalUsername(text, "Из ЧС", ParsedCommandKind.RemoveBlacklist);

        if (text.Equals("Профиль", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(ParsedCommandKind.Profile);

        if (text.StartsWith("Профиль#", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(ParsedCommandKind.Profile, text[8..].Trim());

        if (text.StartsWith("Топ", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = text[3..].Trim();
            
            if (string.IsNullOrEmpty(suffix))
                return new ParsedCommand(ParsedCommandKind.Top);

            if (int.TryParse(suffix, out var limit))
                return new ParsedCommand(ParsedCommandKind.Top, TopLimit: limit);
        }

        return text.Equals("Втопе", StringComparison.OrdinalIgnoreCase) ? new ParsedCommand(ParsedCommandKind.InTop) : null;
    }

    private static ParsedCommand ParseTagAndUsername(string text, string prefix, ParsedCommandKind kind)
    {
        var body = text[prefix.Length..].Trim();
        var parts = body.Split('@', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var tag = parts[0].Trim();
        var username = parts.Length > 1 ? parts[1].Trim().TrimStart('@') : null;
        
        return new ParsedCommand(kind, tag, username);
    }

    private static ParsedCommand ParseOptionalUsername(string text, string prefix, ParsedCommandKind kind)
    {
        var body = text[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(body))
            return new ParsedCommand(kind);

        if (body.StartsWith('@'))
            return new ParsedCommand(kind, TelegramUsername: body.Trim().TrimStart('@'));

        var parts = body.Split('@', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return new ParsedCommand(kind);

        var username = parts[1].Trim().TrimStart('@');
        return new ParsedCommand(kind, TelegramUsername: username);
    }
}