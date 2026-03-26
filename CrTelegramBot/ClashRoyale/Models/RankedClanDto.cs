namespace CrTelegramBot.ClashRoyale.Models;

public sealed class RankedClanDto
{
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ClanScore { get; set; }
    public int ClanWarTrophies { get; set; }
    public int PreviousRank { get; set; }
    public int Rank { get; set; }
    public RankedLocationDto? Location { get; set; }
}