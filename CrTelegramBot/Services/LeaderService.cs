using CrTelegramBot.Configuration;
using Telegram.Bot.Types;

namespace CrTelegramBot.Services;

public sealed class LeaderService
{
    private readonly BotConfig _config;

    public LeaderService(BotConfig config)
    {
        _config = config;
    }

    public bool IsLeader(User? user)
    {
        if (user is null)
            return false;
        
        var username = user.Username?.TrimStart('@') ?? string.Empty;
        
        return _config.LeaderUsernames.Contains(username, StringComparer.OrdinalIgnoreCase);
    }
}