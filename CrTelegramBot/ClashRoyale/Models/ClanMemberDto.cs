namespace CrTelegramBot.ClashRoyale.Models;

public sealed class ClanMemberDto
{
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int Trophies { get; set; }
    public int Donations { get; set; }
}