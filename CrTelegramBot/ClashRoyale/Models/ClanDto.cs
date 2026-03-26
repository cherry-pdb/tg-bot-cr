namespace CrTelegramBot.ClashRoyale.Models;

public sealed class ClanDto
{
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ClanWarTrophies { get; set; }
    public int ClanScore { get; set; }
    public int RequiredTrophies { get; set; }
    public List<ClanMemberDto> MemberList { get; set; } = [];
}