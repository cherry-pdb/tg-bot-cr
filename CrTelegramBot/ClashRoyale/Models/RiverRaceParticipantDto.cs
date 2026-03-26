namespace CrTelegramBot.ClashRoyale.Models;

public sealed class RiverRaceParticipantDto
{
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Fame { get; set; }
    public int RepairPoints { get; set; }
    public int BoatAttacks { get; set; }
    public int DecksUsedToday { get; set; }
    public int DecksUsed { get; set; }
}