using CrTelegramBot.ClashRoyale.Models;

namespace CrTelegramBot.Services;

public sealed class WarService
{
    private readonly ClanService _clanService;

    public WarService(ClanService clanService)
    {
        _clanService = clanService;
    }

    public Task<CurrentRiverRaceDto?> GetStatusAsync(CancellationToken ct = default) => _clanService.GetCurrentRaceAsync(ct);
}