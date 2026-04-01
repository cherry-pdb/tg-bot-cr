using System.Text.Json;
using CrTelegramBot.Configuration;
using CrTelegramBot.Data;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;

namespace CrTelegramBot.Services;

public sealed class LeaderService
{
    private readonly BotConfig _config;
    private readonly BotDbContext _db;
    private const string LeadersSettingKey = "leaders_v1";

    public LeaderService(BotConfig config, BotDbContext db)
    {
        _config = config;
        _db = db;
    }

    public async Task<bool> IsLeaderAsync(User? user, CancellationToken ct)
    {
        if (user is null)
            return false;
        
        var username = user.Username?.TrimStart('@') ?? string.Empty;
        if (_config.LeaderUsernames.Contains(username, StringComparer.OrdinalIgnoreCase))
            return true;

        var current = await GetLeadersAsync(ct);
        if (current.UserIds.Contains(user.Id))
            return true;

        return current.Usernames.Contains(username, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<LeadersSnapshot> GetLeadersAsync(CancellationToken ct)
    {
        var row = await _db.BotSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == LeadersSettingKey, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Value))
            return LeadersSnapshot.Empty;

        try
        {
            var parsed = JsonSerializer.Deserialize<LeadersSetting>(row.Value);
            if (parsed is null)
                return LeadersSnapshot.Empty;

            var usernames = (parsed.Usernames ?? [])
                .Select(NormalizeUsername)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var userIds = (parsed.UserIds ?? [])
                .Where(x => x > 0)
                .Distinct()
                .ToArray();

            return new LeadersSnapshot(userIds, usernames);
        }
        catch
        {
            return LeadersSnapshot.Empty;
        }
    }

    public async Task<(bool changed, LeadersSnapshot current)> AddLeaderAsync(long? telegramUserId, string? telegramUsername, CancellationToken ct)
    {
        var current = await GetLeadersForUpdateAsync(ct);

        var changed = false;
        if (telegramUserId is > 0 && !current.UserIds.Contains(telegramUserId.Value))
        {
            current.UserIds.Add(telegramUserId.Value);
            changed = true;
        }

        var username = NormalizeUsername(telegramUsername);
        if (!string.IsNullOrWhiteSpace(username) && !current.Usernames.Contains(username, StringComparer.OrdinalIgnoreCase))
        {
            current.Usernames.Add(username);
            changed = true;
        }

        if (!changed)
            return (false, current.ToSnapshot());

        await SaveLeadersAsync(current, ct);
        return (true, current.ToSnapshot());
    }

    public async Task<(bool changed, LeadersSnapshot current)> RemoveLeaderAsync(long? telegramUserId, string? telegramUsername, CancellationToken ct)
    {
        var current = await GetLeadersForUpdateAsync(ct);

        var changed = false;
        if (telegramUserId is > 0)
            changed |= current.UserIds.Remove(telegramUserId.Value);

        var username = NormalizeUsername(telegramUsername);
        if (!string.IsNullOrWhiteSpace(username))
        {
            var removedAny = current.Usernames.RemoveAll(x => string.Equals(x, username, StringComparison.OrdinalIgnoreCase)) > 0;
            changed |= removedAny;
        }

        if (!changed)
            return (false, current.ToSnapshot());

        await SaveLeadersAsync(current, ct);
        return (true, current.ToSnapshot());
    }

    private static string NormalizeUsername(string? username) =>
        (username ?? string.Empty).Trim().TrimStart('@');

    private async Task<LeadersMutable> GetLeadersForUpdateAsync(CancellationToken ct)
    {
        var snap = await GetLeadersAsync(ct);
        return new LeadersMutable(snap.UserIds.ToList(), snap.Usernames.ToList());
    }

    private async Task SaveLeadersAsync(LeadersMutable leaders, CancellationToken ct)
    {
        var row = await _db.BotSettings.FirstOrDefaultAsync(x => x.Key == LeadersSettingKey, ct);
        if (row is null)
        {
            row = new Data.Entities.BotSetting { Key = LeadersSettingKey };
            _db.BotSettings.Add(row);
        }

        var payload = new LeadersSetting
        {
            UserIds = leaders.UserIds.Where(x => x > 0).Distinct().OrderBy(x => x).ToArray(),
            Usernames = leaders.Usernames
                .Select(NormalizeUsername)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        row.Value = JsonSerializer.Serialize(payload);
        await _db.SaveChangesAsync(ct);
    }

    private sealed class LeadersSetting
    {
        public long[]? UserIds { get; set; }
        public string[]? Usernames { get; set; }
    }

    private sealed class LeadersMutable
    {
        public List<long> UserIds { get; }
        public List<string> Usernames { get; }

        public LeadersMutable(List<long> userIds, List<string> usernames)
        {
            UserIds = userIds;
            Usernames = usernames;
        }

        public LeadersSnapshot ToSnapshot() => new(UserIds.ToArray(), Usernames.ToArray());
    }
}

public readonly record struct LeadersSnapshot(long[] UserIds, string[] Usernames)
{
    public static LeadersSnapshot Empty => new([], []);
}