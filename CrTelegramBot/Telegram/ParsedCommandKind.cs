namespace CrTelegramBot.Telegram;

public enum ParsedCommandKind
{
    Connect,
    Disconnect,
    EnableNotifications,
    DisableNotifications,
    Commands,
    Participants,
    ParticipantsAll,
    WarStatus,
    RemindWar,
    Blacklist,
    RemoveBlacklist,
    Profile,
    Chests,
    Top,
    InTop,
    AddLeader,
    RemoveLeader,
    Leaders
}