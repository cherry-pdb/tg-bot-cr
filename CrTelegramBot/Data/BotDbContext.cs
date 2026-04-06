using CrTelegramBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrTelegramBot.Data;

public sealed class BotDbContext : DbContext
{
    public BotDbContext(DbContextOptions<BotDbContext> options) : base(options) { }

    public DbSet<UserLink> UserLinks => Set<UserLink>();
    public DbSet<LeaderNotificationSetting> LeaderNotificationSettings => Set<LeaderNotificationSetting>();
    public DbSet<BlacklistedPlayer> BlacklistedPlayers => Set<BlacklistedPlayer>();
    public DbSet<ClanSnapshotMember> ClanSnapshotMembers => Set<ClanSnapshotMember>();
    public DbSet<BotSetting> BotSettings => Set<BotSetting>();
    public DbSet<ChatUser> ChatUsers => Set<ChatUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserLink>().HasIndex(x => x.PlayerTag).IsUnique();
        modelBuilder.Entity<UserLink>().HasIndex(x => x.TelegramUserId);
        modelBuilder.Entity<LeaderNotificationSetting>().HasIndex(x => x.TelegramUserId).IsUnique();
        modelBuilder.Entity<BlacklistedPlayer>().HasIndex(x => x.PlayerTag).IsUnique();
        modelBuilder.Entity<ClanSnapshotMember>().HasIndex(x => x.PlayerTag).IsUnique();
        modelBuilder.Entity<BotSetting>().HasIndex(x => x.Key).IsUnique();
        modelBuilder.Entity<ChatUser>().HasIndex(x => x.TelegramUserId).IsUnique();
        modelBuilder.Entity<ChatUser>().HasIndex(x => x.TelegramUsername);
    }
}