using CrTelegramBot.Data;
using CrTelegramBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

namespace CrTelegramBot.Services;

public sealed class NotificationService
{
    private readonly BotDbContext _db;
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(BotDbContext db, ITelegramBotClient botClient, ILogger<NotificationService> logger)
    {
        _db = db;
        _botClient = botClient;
        _logger = logger;
    }

    public async Task SetLeaderNotificationsAsync(long userId, bool enabled, CancellationToken ct = default)
    {
        var row = await _db.LeaderNotificationSettings.FirstOrDefaultAsync(x => x.TelegramUserId == userId, ct);
        
        if (row is null)
        {
            row = new LeaderNotificationSetting { TelegramUserId = userId };
            _db.LeaderNotificationSettings.Add(row);
        }

        row.IsEnabled = enabled;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<long>> GetEnabledLeaderIdsAsync(CancellationToken ct = default)
    {
        return await _db.LeaderNotificationSettings
            .Where(x => x.IsEnabled)
            .Select(x => x.TelegramUserId)
            .ToListAsync(ct);
    }

    public async Task SendPrivateMessageSafeAsync(long userId, string text, CancellationToken ct = default)
    {
        try
        {
            await _botClient.SendMessage(userId, text, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send message to user {UserId}", userId);
        }
    }
}