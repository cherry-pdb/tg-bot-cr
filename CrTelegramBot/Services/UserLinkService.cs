using CrTelegramBot.ClashRoyale;
using CrTelegramBot.Data;
using CrTelegramBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;

namespace CrTelegramBot.Services;

public sealed class UserLinkService
{
    private readonly BotDbContext _db;

    public UserLinkService(BotDbContext db)
    {
        _db = db;
    }

    public Task<UserLink?> FindByTelegramUserIdAsync(long userId, CancellationToken ct = default) =>
        _db.UserLinks.OrderBy(x => x.Id).FirstOrDefaultAsync(x => x.TelegramUserId == userId, ct);

    public Task<UserLink?> FindByPlayerTagAsync(string playerTag, CancellationToken ct = default) =>
        _db.UserLinks.FirstOrDefaultAsync(x => x.PlayerTag == ClashRoyaleApiClient.NormalizeTag(playerTag), ct);

    public async Task<UserLink> UpsertAsync(User user, string playerTag, CancellationToken ct = default)
    {
        playerTag = ClashRoyaleApiClient.NormalizeTag(playerTag);
        var displayName = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var username = user.Username ?? string.Empty;

        var byTag = await _db.UserLinks.FirstOrDefaultAsync(x => x.PlayerTag == playerTag, ct);

        if (byTag is not null)
        {
            if (byTag.TelegramUserId == user.Id)
            {
                byTag.TelegramUsername = username;
                byTag.TelegramDisplayName = displayName;
                byTag.IsEnabled = true;
                await _db.SaveChangesAsync(ct);
                return byTag;
            }

            _db.UserLinks.Remove(byTag);
        }

        var link = new UserLink
        {
            TelegramUserId = user.Id,
            TelegramUsername = username,
            TelegramDisplayName = displayName,
            PlayerTag = playerTag,
            IsEnabled = true
        };

        _db.UserLinks.Add(link);
        await _db.SaveChangesAsync(ct);

        return link;
    }

    public async Task<UserLink> UpsertAsync(long telegramUserId, string telegramUsername, string telegramDisplayName, string playerTag, CancellationToken ct = default)
    {
        playerTag = ClashRoyaleApiClient.NormalizeTag(playerTag);
        var username = telegramUsername ?? string.Empty;
        var displayName = telegramDisplayName ?? string.Empty;

        var byTag = await _db.UserLinks.FirstOrDefaultAsync(x => x.PlayerTag == playerTag, ct);

        if (byTag is not null)
        {
            if (byTag.TelegramUserId == telegramUserId)
            {
                byTag.TelegramUsername = username;
                byTag.TelegramDisplayName = displayName;
                byTag.IsEnabled = true;
                await _db.SaveChangesAsync(ct);
                return byTag;
            }

            _db.UserLinks.Remove(byTag);
        }

        var link = new UserLink
        {
            TelegramUserId = telegramUserId,
            TelegramUsername = username,
            TelegramDisplayName = displayName,
            PlayerTag = playerTag,
            IsEnabled = true
        };

        _db.UserLinks.Add(link);
        await _db.SaveChangesAsync(ct);

        return link;
    }

    public async Task<bool> RemoveAsync(UserLink link, CancellationToken ct = default)
    {
        _db.UserLinks.Remove(link);
        await _db.SaveChangesAsync(ct);

        return true;
    }
}
