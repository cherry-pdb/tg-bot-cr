namespace CrTelegramBot.ClashRoyale.Models;

public sealed class PlayerDto
{
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ExpLevel { get; set; }
    public int Trophies { get; set; }
    public int BestTrophies { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int BattleCount { get; set; }
    public int ThreeCrownWins { get; set; }
    public int ChallengeCardsWon { get; set; }
    public int ChallengeMaxWins { get; set; }
    public int TournamentCardsWon { get; set; }
    public int TournamentBattleCount { get; set; }
    public int Donations { get; set; }
    public int DonationsReceived { get; set; }
    public int TotalDonations { get; set; }
    public int WarDayWins { get; set; }
    public int ClanCardsCollected { get; set; }
    public int StarPoints { get; set; }
    public int ExpPoints { get; set; }
    public int TotalExpPoints { get; set; }
    public ArenaDto? Arena { get; set; }
    public ClanRefDto? Clan { get; set; }
    public string? Role { get; set; }
    public List<PlayerCardDto> Cards { get; set; } = [];
    public List<PlayerAchievementDto> Achievements { get; set; } = [];
    public List<PlayerBadgeDto> Badges { get; set; } = [];
}