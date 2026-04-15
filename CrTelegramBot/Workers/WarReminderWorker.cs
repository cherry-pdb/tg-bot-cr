using CrTelegramBot.ClashRoyale;
using CrTelegramBot.ClashRoyale.Models;
using CrTelegramBot.Configuration;
using CrTelegramBot.Data;
using CrTelegramBot.Data.Entities;
using CrTelegramBot.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Telegram.Bot;

namespace CrTelegramBot.Workers;

public sealed class WarReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClient _botClient;
    private readonly BotConfig _config;
    private readonly ILogger<WarReminderWorker> _logger;
    private readonly AutoDeleteService _autoDelete;

    public WarReminderWorker(
        IServiceScopeFactory scopeFactory,
        ITelegramBotClient botClient,
        BotConfig config,
        AutoDeleteService autoDelete,
        ILogger<WarReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _botClient = botClient;
        _config = config;
        _autoDelete = autoDelete;
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

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private static bool ShouldRunWarRemindersThisMinute(DateTime utcNow)
    {
        _ = utcNow;
        return true;
    }

    private bool TryGetSummarySchedule(TimeZoneInfo tz, out TimeOnly localTime, out HashSet<DayOfWeek>? days)
    {
        days = null;
        localTime = default;
        
        if (string.IsNullOrWhiteSpace(_config.WarEndDaySummaryLocalTime))
            return false;

        if (!TimeOnly.TryParseExact(_config.WarEndDaySummaryLocalTime.Trim(), "HH:mm", out localTime))
            return false;

        if (_config.WarDaysOfWeek is { Length: > 0 })
        {
            var set = new HashSet<DayOfWeek>();
            
            foreach (var s in _config.WarDaysOfWeek)
                if (Enum.TryParse<DayOfWeek>(s?.Trim(), ignoreCase: true, out var dow))
                    set.Add(dow);
            
            if (set.Count > 0)
                days = set;
        }

        _ = tz;
        return true;
    }

    private bool TryGetLocalSummaryContext(DateTime utcNow, out TimeZoneInfo tz, out DateTime localNow, out TimeOnly localTime, out HashSet<DayOfWeek>? days)
    {
        tz = TimeZoneInfo.Utc;
        localNow = default;
        localTime = default;
        days = null;

        if (string.IsNullOrWhiteSpace(_config.WarEndDaySummaryLocalTime))
            return false;

        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(BotConfig.WarEndDaySummaryTimeZoneId);
        }
        catch
        {
            return false;
        }

        if (!TryGetSummarySchedule(tz, out localTime, out days))
            return false;

        localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);
        
        return days is null || days.Contains(localNow.DayOfWeek);
    }

    private static HashSet<string>? BuildClanMemberTagSet(ClanDto? clan)
    {
        if (clan?.MemberList is not { Count: > 0 })
            return null;

        return clan.MemberList
            .Select(m => ClashRoyaleApiClient.NormalizeTag(m.Tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static List<RiverRaceParticipantDto> FilterParticipantsInCurrentClan(
        IReadOnlyCollection<RiverRaceParticipantDto> participants,
        HashSet<string>? clanMemberTags)
    {
        if (clanMemberTags is null)
            return participants.ToList();

        return participants
            .Where(p => clanMemberTags.Contains(ClashRoyaleApiClient.NormalizeTag(p.Tag)))
            .ToList();
    }

    private HashSet<DayOfWeek> GetWarStartDaysSet()
    {
        if (_config.WarStartDaysOfWeek is { Length: > 0 })
        {
            var set = new HashSet<DayOfWeek>();

            foreach (var s in _config.WarStartDaysOfWeek)
            {
                if (Enum.TryParse<DayOfWeek>(s?.Trim(), ignoreCase: true, out var dow))
                    set.Add(dow);
            }

            if (set.Count > 0)
                return set;
        }

        return [DayOfWeek.Thursday];
    }

    private bool ShouldFireWarStartThisMinute(DateTime utcNow, out string startDayKey)
    {
        startDayKey = "";

        if (!string.IsNullOrWhiteSpace(_config.WarStartLocalTime))
        {
            if (!TimeOnly.TryParseExact(_config.WarStartLocalTime.Trim(), "HH:mm", out var startTime))
                return false;

            TimeZoneInfo tz;

            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(BotConfig.WarEndDaySummaryTimeZoneId);
            }
            catch
            {
                return false;
            }

            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);

            if (!GetWarStartDaysSet().Contains(localNow.DayOfWeek))
                return false;

            var windowStart = localNow.Date + startTime.ToTimeSpan();

            if (localNow >= windowStart && localNow < windowStart.AddMinutes(15))
            {
                startDayKey = localNow.ToString("yyyy-MM-dd");
                return true;
            }

            return false;
        }

        var nowOff = new DateTimeOffset(utcNow, TimeSpan.Zero);

        if (nowOff.Hour != _config.ReminderHourUtc || nowOff.Minute >= 15)
            return false;

        startDayKey = nowOff.UtcDateTime.ToString("yyyy-MM-dd");
        return true;
    }

    private HashSet<DayOfWeek>? TryGetWarDaysSet()
    {
        if (_config.WarDaysOfWeek is not { Length: > 0 })
            return null;

        var set = new HashSet<DayOfWeek>();

        foreach (var s in _config.WarDaysOfWeek)
        {
            if (Enum.TryParse<DayOfWeek>(s?.Trim(), ignoreCase: true, out var dow))
                set.Add(dow);
        }

        return set.Count > 0 ? set : null;
    }

    private async Task<long> GetMainChatIdAsync(BotDbContext db, CancellationToken ct)
    {
        var row = await db.BotSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == BotConfig.MainChatIdSettingKey, ct);

        if (row is not null && long.TryParse(row.Value, out var id))
            return id;

        return _config.MainChatId;
    }

    private bool ShouldFireWarNudgeThisMinute(DateTime utcNow, int hoursBeforeEnd)
    {
        if (string.IsNullOrWhiteSpace(_config.WarEndLocalTime))
            return false;

        if (!TimeOnly.TryParseExact(_config.WarEndLocalTime.Trim(), "HH:mm", out var endTime))
            return false;

        TimeZoneInfo tz;

        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(BotConfig.WarEndDaySummaryTimeZoneId);
        }
        catch
        {
            return false;
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);

        var allowedDays = TryGetWarDaysSet();
        if (allowedDays is not null && !allowedDays.Contains(localNow.DayOfWeek))
            return false;

        var endLocal = localNow.Date + endTime.ToTimeSpan();
        var nudgeLocal = endLocal.AddHours(-hoursBeforeEnd);

        return localNow.Hour == nudgeLocal.Hour
               && localNow.Minute < 15
               && localNow.Date == nudgeLocal.Date;
    }

    private static string ComputeDecksSignature(IReadOnlyCollection<RiverRaceParticipantDto> participants)
    {
        var parts = participants
            .Where(p => !string.IsNullOrWhiteSpace(p.Tag))
            .Select(p => $"{p.Tag.Trim().ToUpperInvariant()}={p.DecksUsedToday}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var joined = string.Join('|', parts);
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(joined));
        
        return Convert.ToHexString(bytes);
    }

    private static bool LooksLikeDayReset(int prevTotal, int prevMax, int curTotal, int curMax)
    {
        if (prevTotal >= 30 && curTotal <= Math.Max(2, prevTotal / 10))
            return true;

        return prevMax >= 1 && curMax == 0 && prevTotal >= 10;
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var clanService = scope.ServiceProvider.GetRequiredService<ClanService>();
        var enabled = await db.BotSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == "war_reminder_enabled", ct);
        
        if (enabled?.Value != "1")
            return;

        var mainChatId = await GetMainChatIdAsync(db, ct);
        var utcNow = DateTime.UtcNow;
        
        if (!ShouldRunWarRemindersThisMinute(utcNow))
            return;

        var now = DateTimeOffset.UtcNow;
        var dayKey = now.ToString("yyyy-MM-dd");

        if (ShouldFireWarStartThisMinute(utcNow, out var warStartDayKey))
        {
            var marker = $"war_start_{warStartDayKey}";
            
            if (!await db.BotSettings.AnyAsync(x => x.Key == marker, ct))
            {
                var sent = await _botClient.SendMessage(mainChatId, "⚔️ Началось КВ. Не забудьте отыграть колоды.", cancellationToken: ct);
                _autoDelete.ScheduleDelete(mainChatId, sent.MessageId, TimeSpan.FromHours(1));
                db.BotSettings.Add(new BotSetting { Key = marker, Value = "1" });
                await db.SaveChangesAsync(ct);
            }
        }

        foreach (var hoursBeforeEnd in _config.WarNudgesHoursBeforeEnd.Distinct().OrderByDescending(x => x))
        {
            var useLocalWarEnd = !string.IsNullOrWhiteSpace(_config.WarEndLocalTime);

            if (useLocalWarEnd)
            {
                if (!ShouldFireWarNudgeThisMinute(utcNow, hoursBeforeEnd))
                    continue;
            }
            else
            {
                var targetHour = _config.WarEndHourUtc - hoursBeforeEnd;
            
                if (targetHour is < 0 or > 23)
                    continue;

                if (now.Hour != targetHour || now.Minute >= 15)
                    continue;
            }

            var marker = $"war_nudge_{hoursBeforeEnd}_{dayKey}";
            
            if (await db.BotSettings.AnyAsync(x => x.Key == marker, ct))
                continue;

            var race = await clanService.GetCurrentRaceAsync(ct);
            var clan = await clanService.GetClanAsync(ct);
            var clanTags = BuildClanMemberTagSet(clan);
            var our = race?.Clan;
            
            if (our?.Participants is null || our.Participants.Count == 0)
                continue;

            var participantsInClan = FilterParticipantsInCurrentClan(our.Participants, clanTags);

            if (participantsInClan.Count == 0)
                continue;

            var needToPlay = participantsInClan
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

            var sent = await _botClient.SendMessage(mainChatId, text, cancellationToken: ct);
            _autoDelete.ScheduleDelete(mainChatId, sent.MessageId, TimeSpan.FromHours(1));
            db.BotSettings.Add(new BotSetting { Key = marker, Value = "1" });
            await db.SaveChangesAsync(ct);
        }

        if (TryGetLocalSummaryContext(utcNow, out var tz, out var localNow, out var localTime, out var days)
            && (localNow.Hour > localTime.Hour || (localNow.Hour == localTime.Hour && localNow.Minute >= localTime.Minute)))
        {
            var localDateKey = localNow.ToString("yyyy-MM-dd");
            var doneKey = $"war_eod_done_{localDateKey}";
            var sigKey = $"war_eod_sig_{localDateKey}";
            var textKey = $"war_eod_text_{localDateKey}";
            var totalsKey = $"war_eod_totals_{localDateKey}";

            if (!await db.BotSettings.AsNoTracking().AnyAsync(x => x.Key == doneKey, ct))
            {
                var race = await clanService.GetCurrentRaceAsync(ct);
                var clan = await clanService.GetClanAsync(ct);
                var clanTags = BuildClanMemberTagSet(clan);
                var our = race?.Clan;

                if (our?.Participants is not null && our.Participants.Count > 0)
                {
                    var participantsInClan = FilterParticipantsInCurrentClan(our.Participants, clanTags);

                    if (participantsInClan.Count > 0)
                    {
                    var curTotal = participantsInClan.Sum(p => p.DecksUsedToday);
                    var curMax = participantsInClan.Max(p => p.DecksUsedToday);
                    var curSig = ComputeDecksSignature(participantsInClan);
                    var prevSig = await db.BotSettings
                        .AsNoTracking()
                        .Where(x => x.Key == sigKey)
                        .Select(x => x.Value)
                        .FirstOrDefaultAsync(ct);
                    var prevTotals = await db.BotSettings
                        .AsNoTracking()
                        .Where(x => x.Key == totalsKey)
                        .Select(x => x.Value)
                        .FirstOrDefaultAsync(ct);
                    var prevText = await db.BotSettings
                        .AsNoTracking()
                        .Where(x => x.Key == textKey)
                        .Select(x => x.Value)
                        .FirstOrDefaultAsync(ct);
                    var prevTotal = 0;
                    var prevMax = 0;
                    
                    if (!string.IsNullOrWhiteSpace(prevTotals))
                    {
                        var split = prevTotals.Split(';');
                        _ = split.Length > 0 && int.TryParse(split[0], out prevTotal);
                        _ = split.Length > 1 && int.TryParse(split[1], out prevMax);
                    }

                    if (!string.IsNullOrWhiteSpace(prevSig) && !string.Equals(prevSig, curSig, StringComparison.OrdinalIgnoreCase)
                        && LooksLikeDayReset(prevTotal, prevMax, curTotal, curMax))
                    {
                        var toSend = string.IsNullOrWhiteSpace(prevText)
                            ? "📋 Сводка по КВ: не удалось восстановить снимок до сброса."
                            : prevText;

                        var sent = await _botClient.SendMessage(mainChatId, toSend, cancellationToken: ct);
                        _autoDelete.ScheduleDelete(mainChatId, sent.MessageId, TimeSpan.FromHours(2));
                        db.BotSettings.Add(new BotSetting { Key = doneKey, Value = "1" });
                        await db.SaveChangesAsync(ct);
                    }
                    else
                    {
                        var links = await db.UserLinks.AsNoTracking()
                            .Where(x => x.IsEnabled)
                            .ToDictionaryAsync(x => x.PlayerTag, StringComparer.OrdinalIgnoreCase, ct);

                        var lines = participantsInClan
                            .Select(p =>
                            {
                                var left = Math.Clamp(4 - p.DecksUsedToday, 0, 4);
                                return (Participant: p, Left: left);
                            })
                            .Where(x => x.Left > 0)
                            .OrderByDescending(x => x.Left)
                            .ThenBy(x => x.Participant.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(x =>
                            {
                                var p = x.Participant;

                                return links.TryGetValue(p.Tag, out var link)
                                    ? $"• {p.Name} / id:{link.TelegramUserId} — осталось {x.Left} из 4"
                                    : $"• {p.Name} — осталось {x.Left} из 4";
                            })
                            .ToList();

                        var header = $"📋 Сводка по КВ (последняя минута перед сбросом, МСК {localNow:HH:mm}):\n";
                        var text = lines.Count > 0
                            ? header + string.Join('\n', lines)
                            : header + "Все участники отыграли 4 колоды.";

                        async Task UpsertSettingAsync(string key, string value)
                        {
                            var existing = await db.BotSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
                            if (existing is null)
                                db.BotSettings.Add(new BotSetting { Key = key, Value = value });
                            else
                                existing.Value = value;
                        }

                        await UpsertSettingAsync(sigKey, curSig);
                        await UpsertSettingAsync(totalsKey, $"{curTotal};{curMax}");
                        await UpsertSettingAsync(textKey, text);
                        await db.SaveChangesAsync(ct);
                    }
                    }
                }
            }
        }
    }
}