namespace CrTelegramBot.ClashRoyale.Models;

public sealed class RiverRaceClanDto
{
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    // В ответах некоторых endpoint'ов (например, riverracelog) клан приходит вложенным объектом `clan`.
    public RiverRaceClanRefDto? Clan { get; set; }
    public int Fame { get; set; }
    public int RepairPoints { get; set; }
    public int DecksUsed { get; set; }
    public int DecksUsedToday { get; set; }
    public int BoatAttacks { get; set; }
    public int Crowns { get; set; }
    public int PeriodPoints { get; set; }
    public List<RiverRaceParticipantDto> Participants { get; set; } = [];
}