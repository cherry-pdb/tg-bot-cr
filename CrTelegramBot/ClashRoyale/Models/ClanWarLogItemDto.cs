namespace CrTelegramBot.ClashRoyale.Models;

public sealed class ClanWarLogItemDto
{
    public string? CreatedDate { get; set; }
    public int SeasonId { get; set; }
    public int SectionIndex { get; set; }
    public List<RiverRaceClanDto> Standings { get; set; } = [];
}