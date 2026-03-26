using CrTelegramBot.ClashRoyale;
using CrTelegramBot.ClashRoyale.Models;
using CrTelegramBot.Configuration;

namespace CrTelegramBot.Services;

public sealed class ClanService
{
    private readonly ClashRoyaleApiClient _api;
    private readonly BotConfig _config;

    public ClanService(ClashRoyaleApiClient api, BotConfig config)
    {
        _api = api;
        _config = config;
    }

    public Task<ClanDto?> GetClanAsync(CancellationToken ct = default) => _api.GetClanAsync(_config.ClanTag, ct);
    public Task<CurrentRiverRaceDto?> GetCurrentRaceAsync(CancellationToken ct = default) => _api.GetCurrentRiverRaceAsync(_config.ClanTag, ct);
    public Task<ClanWarLogDto?> GetWarLogAsync(int limit = 10, CancellationToken ct = default) => _api.GetClanWarLogAsync(_config.ClanTag, limit, ct);
}