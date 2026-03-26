using CrTelegramBot.ClashRoyale;
using CrTelegramBot.Configuration;
using CrTelegramBot.Data;
using CrTelegramBot.Data.Entities;
using CrTelegramBot.Services;
using Microsoft.EntityFrameworkCore;

namespace CrTelegramBot.Workers;

public sealed class ClanMembershipMonitorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ClashRoyaleApiClient _api;
    private readonly BotConfig _config;
    private readonly ILogger<ClanMembershipMonitorWorker> _logger;

    public ClanMembershipMonitorWorker(
        IServiceScopeFactory scopeFactory,
        ClashRoyaleApiClient api,
        BotConfig config,
        ILogger<ClanMembershipMonitorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _api = api;
        _config = config;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalSeconds = _config.ClanMonitorIntervalSeconds;
            
            if (intervalSeconds <= 0)
                intervalSeconds = 60;

            try
            {
                await CheckClanChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Clan monitor error");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }

    private async Task CheckClanChangesAsync(CancellationToken ct)
    {
        var clan = await _api.GetClanAsync(_config.ClanTag, ct);
        
        if (clan is null)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var current = clan.MemberList.ToDictionary(x => x.Tag, StringComparer.OrdinalIgnoreCase);
        var previous = await db.ClanSnapshotMembers.ToListAsync(ct);
        var previousMap = previous.ToDictionary(x => x.PlayerTag, StringComparer.OrdinalIgnoreCase);
        var blacklist = await db.BlacklistedPlayers.ToDictionaryAsync(x => x.PlayerTag, StringComparer.OrdinalIgnoreCase, ct);
        var joined = current.Keys.Except(previousMap.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var left = previousMap.Keys.Except(current.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var leaderIds = await notifications.GetEnabledLeaderIdsAsync(ct);

        foreach (var tag in joined)
        {
            var player = current[tag];
            var warn = blacklist.ContainsKey(tag) ? "\n⛔ ВНИМАНИЕ: игрок в ЧС." : string.Empty;
            
            foreach (var leaderId in leaderIds)
                await notifications.SendPrivateMessageSafeAsync(leaderId, $"🟢 В клан вошёл {player.Name} {tag}{warn}", ct);
        }

        foreach (var tag in left)
        {
            var player = previousMap[tag];
            
            foreach (var leaderId in leaderIds)
                await notifications.SendPrivateMessageSafeAsync(leaderId, $"🔴 Клан покинул {player.PlayerName} {tag}", ct);
        }

        db.ClanSnapshotMembers.RemoveRange(previous);
        db.ClanSnapshotMembers.AddRange(clan.MemberList.Select(x => new ClanSnapshotMember
        {
            PlayerTag = x.Tag,
            PlayerName = x.Name,
            LastSeenAt = DateTimeOffset.UtcNow
        }));

        await db.SaveChangesAsync(ct);
    }
}
