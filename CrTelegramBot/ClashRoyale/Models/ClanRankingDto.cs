namespace CrTelegramBot.ClashRoyale.Models;

public sealed class ClanRankingDto
{
    public List<RankedClanDto> Items { get; set; } = [];
    public RankedPagingDto? Paging { get; set; }
}