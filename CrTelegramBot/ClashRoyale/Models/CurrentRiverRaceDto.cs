namespace CrTelegramBot.ClashRoyale.Models;

public sealed class CurrentRiverRaceDto
{
    public RiverRaceClanDto? Clan { get; set; }
    public List<RiverRaceClanDto> Clans { get; set; } = [];
    public int? SectionIndex { get; set; }
    public string? State { get; set; }
    public string? PeriodType { get; set; }
}