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
        _db.UserLinks.FirstOrDefaultAsync(x => x.TelegramUserId == userId, ct);

    public Task<UserLink?> FindByPlayerTagAsync(string playerTag, CancellationToken ct = default) =>
        _db.UserLinks.FirstOrDefaultAsync(x => x.PlayerTag == ClashRoyaleApiClient.NormalizeTag(playerTag), ct);

    public async Task<UserLink> UpsertAsync(User user, string playerTag, CancellationToken ct = default)
    {
        playerTag = ClashRoyaleApiClient.NormalizeTag(playerTag);
        var byUser = await _db.UserLinks.FirstOrDefaultAsync(x => x.TelegramUserId == user.Id, ct);
        
        if (byUser is not null)
            _db.UserLinks.Remove(byUser);

        var byTag = await _db.UserLinks.FirstOrDefaultAsync(x => x.PlayerTag == playerTag, ct);
        
        if (byTag is not null)
            _db.UserLinks.Remove(byTag);

        var link = new UserLink
        {
            TelegramUserId = user.Id,
            TelegramUsername = user.Username ?? string.Empty,
            TelegramDisplayName = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
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
        var byUser = await _db.UserLinks.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);
        
        if (byUser is not null)
            _db.UserLinks.Remove(byUser);

        var byTag = await _db.UserLinks.FirstOrDefaultAsync(x => x.PlayerTag == playerTag, ct);
        
        if (byTag is not null)
            _db.UserLinks.Remove(byTag);

        var link = new UserLink
        {
            TelegramUserId = telegramUserId,
            TelegramUsername = telegramUsername ?? string.Empty,
            TelegramDisplayName = telegramDisplayName ?? string.Empty,
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