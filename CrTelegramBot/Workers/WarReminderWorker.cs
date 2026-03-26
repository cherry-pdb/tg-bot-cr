using CrTelegramBot.Configuration;
using CrTelegramBot.Data;
using CrTelegramBot.Data.Entities;
using CrTelegramBot.Services;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

namespace CrTelegramBot.Workers;

public sealed class WarReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClient _botClient;
    private readonly BotConfig _config;
    private readonly ILogger<WarReminderWorker> _logger;

    public WarReminderWorker(
        IServiceScopeFactory scopeFactory,
        ITelegramBotClient botClient,
        BotConfig config,
        ILogger<WarReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _botClient = botClient;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "War reminder error");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var clanService = scope.ServiceProvider.GetRequiredService<ClanService>();
        var enabled = await db.BotSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == "war_reminder_enabled", ct);
        
        if (enabled?.Value != "1")
            return;

        var now = DateTimeOffset.UtcNow;
        var dayKey = now.ToString("yyyy-MM-dd");

        if (now.Hour == _config.ReminderHourUtc && now.Minute < 15)
        {
            var marker = $"war_start_{dayKey}";
            
            if (!await db.BotSettings.AnyAsync(x => x.Key == marker, ct))
            {
                await _botClient.SendMessage(_config.MainChatId, "⚔️ Началось КВ. Не забудьте отыграть колоды.", cancellationToken: ct);
                db.BotSettings.Add(new BotSetting { Key = marker, Value = "1" });
                await db.SaveChangesAsync(ct);
            }
        }

        foreach (var hoursBeforeEnd in _config.WarNudgesHoursBeforeEnd.Distinct().OrderByDescending(x => x))
        {
            var targetHour = _config.WarEndHourUtc - hoursBeforeEnd;
            
            if (targetHour is < 0 or > 23)
                continue;

            if (now.Hour != targetHour || now.Minute >= 15)
                continue;

            var marker = $"war_nudge_{hoursBeforeEnd}_{dayKey}";
            
            if (await db.BotSettings.AnyAsync(x => x.Key == marker, ct))
                continue;

            var race = await clanService.GetCurrentRaceAsync(ct);
            var our = race?.Clan;
            
            if (our?.Participants is null || our.Participants.Count == 0)
                continue;

            var needToPlay = our.Participants
                .Where(p => p.DecksUsedToday < 4)
                .OrderBy(p => p.DecksUsedToday)
                .ToList();

            if (needToPlay.Count == 0)
            {
                db.BotSettings.Add(new BotSetting { Key = marker, Value = "1" });
                await db.SaveChangesAsync(ct);
                continue;
            }

            var links = await db.UserLinks.AsNoTracking()
                .Where(x => x.IsEnabled)
                .ToDictionaryAsync(x => x.PlayerTag, StringComparer.OrdinalIgnoreCase, ct);

            var mentions = needToPlay.Select(p =>
            {
                if (links.TryGetValue(p.Tag, out var link) && !string.IsNullOrWhiteSpace(link.TelegramUsername))
                    return $"@{link.TelegramUsername.Trim().TrimStart('@')}";
                
                return p.Name;
            });

            var text =
                $"⏰ До конца дня КВ осталось примерно {hoursBeforeEnd}ч.\n" +
                $"Кто ещё не доиграл 4 колоды (сегодня):\n" +
                string.Join(" ", mentions);

            await _botClient.SendMessage(_config.MainChatId, text, cancellationToken: ct);
            db.BotSettings.Add(new BotSetting { Key = marker, Value = "1" });
            await db.SaveChangesAsync(ct);
        }
    }
}