using System.Text;
using CrTelegramBot.ClashRoyale;
using CrTelegramBot.ClashRoyale.Models;
using CrTelegramBot.Configuration;
using CrTelegramBot.Data;
using CrTelegramBot.Data.Entities;
using CrTelegramBot.Services;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CrTelegramBot.Telegram;

public sealed class BotUpdateHandler : IUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ClashRoyaleApiClient _api;
    private readonly BotConfig _config;
    private readonly CommandParser _parser;
    private readonly ILogger<BotUpdateHandler> _logger;

    public BotUpdateHandler(
        ITelegramBotClient botClient,
        IServiceScopeFactory scopeFactory,
        ClashRoyaleApiClient api,
        BotConfig config,
        CommandParser parser,
        ILogger<BotUpdateHandler> logger)
    {
        _botClient = botClient;
        _scopeFactory = scopeFactory;
        _api = api;
        _config = config;
        _parser = parser;
        _logger = logger;
    }
    
    public Task HandleErrorAsync(ITelegramBotClient _, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        _logger.LogError(exception, "Telegram polling error {Source}", source);
        
        return Task.CompletedTask;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        if (update.ChatMember is not null)
        {
            await HandleChatMemberAsync(update.ChatMember, ct);
            
            return;
        }
// ---------------------------------------------------------------------------------------------------------------------
        if (update.Message is not null)
        {
            var mes = update.Message;

            if (mes.Text == "chatId")
            {
                _config.MainChatId = mes.Chat.Id;
                
                await botClient.SendMessage(
                    mes.Chat.Id,
                    "Updated successfully",
                    cancellationToken: ct);
            }
        }
// ---------------------------------------------------------------------------------------------------------------------
        if (update.Message is not { Text: { } text } message)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var leaderService = scope.ServiceProvider.GetRequiredService<LeaderService>();
        var userLinkService = scope.ServiceProvider.GetRequiredService<UserLinkService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var clanService = scope.ServiceProvider.GetRequiredService<ClanService>();
        var warService = scope.ServiceProvider.GetRequiredService<WarService>();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        await UpsertSeenUserAsync(message, db, ct);

        if (TryHandleFunReply(text, out var funReply))
        {
            await _botClient.SendMessage(message.Chat.Id, funReply, cancellationToken: ct);
            return;
        }

        var command = _parser.Parse(text);
        
        if (command is null)
            return;

        var isLeader = leaderService.IsLeader(message.From);
        
        switch (command.Kind)
        {
            case ParsedCommandKind.Connect:
                if (!isLeader) { await DenyAsync(message.Chat.Id, ct); return; }
                await HandleConnectAsync(message, command, userLinkService, ct);
                break;

            case ParsedCommandKind.Disconnect:
                if (!isLeader) { await DenyAsync(message.Chat.Id, ct); return; }
                await HandleDisconnectAsync(message, command, db, userLinkService, ct);
                break;

            case ParsedCommandKind.EnableNotifications:
                if (!isLeader) { await DenyAsync(message.Chat.Id, ct); return; }
                await notificationService.SetLeaderNotificationsAsync(message.From!.Id, true, ct);
                await _botClient.SendMessage(message.Chat.Id, "✅ Личные уведомления включены.", cancellationToken: ct);
                break;

            case ParsedCommandKind.DisableNotifications:
                if (!isLeader) { await DenyAsync(message.Chat.Id, ct); return; }
                await notificationService.SetLeaderNotificationsAsync(message.From!.Id, false, ct);
                await _botClient.SendMessage(message.Chat.Id, "✅ Личные уведомления отключены.", cancellationToken: ct);
                break;

            case ParsedCommandKind.Commands:
                await _botClient.SendMessage(
                    message.Chat.Id,
                    BuildCommandsHelpDetailed(isLeader, message.Chat.Type),
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);
                break;

            case ParsedCommandKind.Participants:
                if (!isLeader) { await DenyAsync(message.Chat.Id, ct); return; }
                await HandleParticipantsAsync(message.Chat.Id, clanService, db, ct);
                break;

            case ParsedCommandKind.WarStatus:
                if (!isLeader) { await DenyAsync(message.Chat.Id, ct); return; }
                await HandleWarStatusAsync(message.Chat.Id, warService, ct);
                break;

            case ParsedCommandKind.RemindWar:
                if (!isLeader) { await DenyAsync(message.Chat.Id, ct); return; }
                await SetWarReminderEnabledAsync(db, true, ct);
                await _botClient.SendMessage(message.Chat.Id, "✅ Напоминания о КВ включены.", cancellationToken: ct);
                break;

            case ParsedCommandKind.Blacklist:
                if (!isLeader || message.Chat.Type != ChatType.Private) { await DenyAsync(message.Chat.Id, ct); return; }
                await HandleBlacklistAsync(command.PlayerTag!, db, ct);
                await _botClient.SendMessage(message.Chat.Id, $"⛔ Игрок {ClashRoyaleApiClient.NormalizeTag(command.PlayerTag!)} добавлен в ЧС.", cancellationToken: ct);
                break;

            case ParsedCommandKind.Profile:
                await HandleProfileAsync(message, command, db, ct);
                break;

            case ParsedCommandKind.Chests:
                await HandleChestsAsync(message, command, db, ct);
                break;

            case ParsedCommandKind.Top:
                await HandleTopAsync(message.Chat.Id, command.TopLimit, ct);
                break;
            case ParsedCommandKind.InTop:
                await HandleInTopAsync(message.Chat.Id, ct);
                break;
        }
    }

    private static string BuildCommandsHelp(bool isLeader, ChatType chatType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("📌 Доступные команды");
        sb.AppendLine();
        sb.AppendLine("Краткий список. Нажми «Подробнее» ниже, чтобы увидеть описание каждой команды.");
        sb.AppendLine();
        sb.AppendLine("Для всех:");
        sb.AppendLine("- Профиль / Профиль#ТЕГ");
        sb.AppendLine("- Сундуки / Сундуки#ТЕГ");
        sb.AppendLine("- Топ / Топ50");
        sb.AppendLine("- Втопе");
        sb.AppendLine("- Команды");
        sb.AppendLine();
        sb.AppendLine("Только для руководителей:");
        sb.AppendLine("- Подключить#ТЕГ (ответом на сообщение)");
        sb.AppendLine("- Подключить#ТЕГ@username");
        sb.AppendLine("- Отключить#ТЕГ (или ответом на сообщение)");
        sb.AppendLine("- Отключить#ТЕГ@username");
        sb.AppendLine("- Включить уведомления");
        sb.AppendLine("- Отключить уведомления");
        sb.AppendLine("- Участники");
        sb.AppendLine("- Что с КВ? / Что с КВ");
        sb.AppendLine("- Напомни о КВ");

        sb.AppendLine(chatType == ChatType.Private ? "- В ЧС#ТЕГ" : "- В ЧС#ТЕГ (только в ЛС боту)");

        return sb.ToString().TrimEnd();
    }
    
    private static string BuildCommandsHelpDetailed(bool isLeader, ChatType chatType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("📌 Доступные команды (подробно)");
        sb.AppendLine();
        sb.AppendLine("Для всех:");
        sb.AppendLine("• <b><u>Профиль</u></b> — показать профиль подключённого игрока (через Подключить/Привязать).");
        sb.AppendLine("• <b><u>Профиль#ТЕГ</u></b> — показать профиль игрока по тегу без привязки.");
        sb.AppendLine("• <b><u>Сундуки</u></b> — показать очередь сундуков для подключённого профиля.");
        sb.AppendLine("• <b><u>Сундуки#ТЕГ</u></b> — показать очередь сундуков по тегу без привязки.");
        sb.AppendLine("• <b><u>Топ / ТопN</u></b> — показать топ N кланов по кубкам в регионе (по умолчанию топ30).");
        sb.AppendLine("• <b><u>Втопе</u></b> — показать место клана в топе по кубкам и по КВ.");
        sb.AppendLine("• <b><u>Команды</u></b> — краткий список команд и их описание.");
        sb.AppendLine();
        sb.AppendLine("Только для руководителей:");
        sb.AppendLine("• <b><u>Подключить#ТЕГ (ответом на сообщение)</u></b> — привязать Telegram-пользователя к тегу игрока.");
        sb.AppendLine("• <b><u>Подключить#ТЕГ@username</u></b> — привязать игрока по тегу к пользователю @username.");
        sb.AppendLine("• <b><u>Отключить#ТЕГ</u></b> — снять привязку по тегу игрока.");
        sb.AppendLine("• <b><u>Отключить#ТЕГ@username</u></b> — снять привязку по нику Telegram.");
        sb.AppendLine("• <b><u>Включить уведомления</u></b> — включить личные уведомления о входе/выходе игроков и напоминаниях.");
        sb.AppendLine("• <b><u>Отключить уведомления</u></b> — отключить личные уведомления для руководителя.");
        sb.AppendLine("• <b><u>Участники</u></b> — список участников клана с отметкой, кто привязан к Telegram.");
        sb.AppendLine("• <b><u>Что с КВ</u></b> — текущий статус КВ и кланы в рейсе.");
        sb.AppendLine("• <b><u>Напомни о КВ</u></b> — включить автоматические напоминания о КВ в основной чат.");

        sb.AppendLine(chatType == ChatType.Private
            ? "• <b><u>В ЧС#ТЕГ</u></b> — добавить игрока по тегу в чёрный список (будут приходить предупреждения при его входе в клан)."
            : "• <b><u>В ЧС#ТЕГ</u></b> — добавить игрока в ЧС (команда работает только в ЛС боту).");

        if (!isLeader)
        {
            sb.AppendLine();
            sb.AppendLine("Часть команд выше доступна только руководителям клана.");
        }

        return sb.ToString().TrimEnd();
    }
    
    private async Task HandleConnectAsync(Message message, ParsedCommand command, UserLinkService userLinkService, CancellationToken ct)
    {
        var targetUser = message.ReplyToMessage?.From;
        var player = await _api.GetPlayerAsync(command.PlayerTag!, ct);
        
        if (player is null)
        {
            await _botClient.SendMessage(message.Chat.Id, "Игрок не найден в Clash Royale API.", cancellationToken: ct);
            return;
        }

        if (targetUser is not null)
        {
            await userLinkService.UpsertAsync(targetUser, command.PlayerTag!, ct);
        }
        else if (!string.IsNullOrWhiteSpace(command.TelegramUsername))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            var username = command.TelegramUsername.Trim().TrimStart('@');
            var usernameLower = username.ToLower();
            var seenCandidates = await db.ChatUsers.AsNoTracking()
                .Where(x => x.TelegramUsername != "" && x.TelegramUsername.ToLower() == usernameLower)
                .ToListAsync(ct);
            var seen = seenCandidates
                .OrderByDescending(x => x.LastSeenAt)
                .FirstOrDefault();

            if (seen is null)
            {
                await _botClient.SendMessage(message.Chat.Id, $"Не вижу пользователя @{username} в истории сообщений. Пусть он напишет в чат, либо подключай ответом на сообщение.", cancellationToken: ct);
                return;
            }

            await userLinkService.UpsertAsync(seen.TelegramUserId, seen.TelegramUsername, seen.TelegramDisplayName, command.PlayerTag!, ct);
        }
        else
        {
            await _botClient.SendMessage(message.Chat.Id, "Используй: ответом на сообщение 'Подключить#ТЕГ' или в чат 'Подключить#ТЕГ@username'.", cancellationToken: ct);
            return;
        }

        await _botClient.SendMessage(message.Chat.Id, $"✅ Подключено: {player.Name} {player.Tag}", cancellationToken: ct);
    }

    private async Task HandleDisconnectAsync(Message message, ParsedCommand command, BotDbContext db, UserLinkService userLinkService, CancellationToken ct)
    {
        UserLink? link = null;

        if (!string.IsNullOrWhiteSpace(command.PlayerTag))
            link = await db.UserLinks.FirstOrDefaultAsync(x => x.PlayerTag == ClashRoyaleApiClient.NormalizeTag(command.PlayerTag), ct);

        if (link is null && !string.IsNullOrWhiteSpace(command.TelegramUsername))
        {
            var username = command.TelegramUsername.Trim().TrimStart('@');
            var usernameLower = username.ToLower();
            var seenCandidates = await db.ChatUsers.AsNoTracking()
                .Where(x => x.TelegramUsername != "" && x.TelegramUsername.ToLower() == usernameLower)
                .ToListAsync(ct);
            var seen = seenCandidates
                .OrderByDescending(x => x.LastSeenAt)
                .FirstOrDefault();
            
            if (seen is not null)
                link = await db.UserLinks.FirstOrDefaultAsync(x => x.TelegramUserId == seen.TelegramUserId, ct);
        }

        if (link is null && message.ReplyToMessage?.From is not null)
            link = await db.UserLinks.FirstOrDefaultAsync(x => x.TelegramUserId == message.ReplyToMessage.From.Id, ct);

        if (link is null)
        {
            await _botClient.SendMessage(message.Chat.Id, "Связка не найдена.", cancellationToken: ct);
            return;
        }

        await userLinkService.RemoveAsync(link, ct);
        await _botClient.SendMessage(message.Chat.Id, $"✅ Отключено: {link.TelegramDisplayName} ← {link.PlayerTag}", cancellationToken: ct);
    }

    private static async Task UpsertSeenUserAsync(Message message, BotDbContext db, CancellationToken ct)
    {
        var from = message.From;
        
        if (from is null)
            return;

        var username = from.Username?.Trim().TrimStart('@') ?? string.Empty;
        var displayName = string.Join(' ', new[] { from.FirstName, from.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var existing = await db.ChatUsers.FirstOrDefaultAsync(x => x.TelegramUserId == from.Id, ct);
        
        if (existing is null)
        {
            db.ChatUsers.Add(new ChatUser
            {
                TelegramUserId = from.Id,
                TelegramUsername = username,
                TelegramDisplayName = displayName,
                LastSeenAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.TelegramUsername = username;
            existing.TelegramDisplayName = displayName;
            existing.LastSeenAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task HandleParticipantsAsync(long chatId, ClanService clanService, BotDbContext db, CancellationToken ct)
    {
        var clan = await clanService.GetClanAsync(ct);

        if (clan is null)
        {
            await _botClient.SendMessage(chatId, "Не удалось загрузить состав клана.", cancellationToken: ct);
            return;
        }

        var links = await db.UserLinks.AsNoTracking().ToListAsync(ct);
        var linkedByTag = links.ToDictionary(x => x.PlayerTag, StringComparer.OrdinalIgnoreCase);

        static string? GetTelegramLink(UserLink? link)
        {
            if (link is null)
                return null;

            if (link.TelegramUserId > 0)
                return $"tg://user?id={link.TelegramUserId}";

            var username = link.TelegramUsername.Trim();
            
            if (string.IsNullOrWhiteSpace(username))
                return null;

            username = username.TrimStart('@');
            
            if (string.IsNullOrWhiteSpace(username))
                return null;

            return $"tg://resolve?domain={username}";
        }

        var lines = clan.MemberList
            .Select(member =>
            {
                linkedByTag.TryGetValue(member.Tag, out var linked);
                var isLinked = linked is not null;
                var nameEsc = System.Net.WebUtility.HtmlEncode(member.Name);
                var tagEsc = System.Net.WebUtility.HtmlEncode(member.Tag);
                var telegramLink = isLinked ? GetTelegramLink(linked) : null;
                var text = isLinked
                    ? telegramLink is not null
                        ? $"✅ - <a href=\"{telegramLink}\">{nameEsc}</a> {tagEsc}"
                        : $"✅ - {nameEsc} {tagEsc}"
                    : $"❌ - {nameEsc} {tagEsc}";

                return new { isLinked, member.Name, member.Tag, text };
            })
            .OrderByDescending(x => x.isLinked)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.text)
            .ToList();

        await _botClient.SendMessage(
            chatId,
            "👥 Участники клана\n\n" + string.Join("\n", lines.Take(50)),
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }
    
    private async Task HandleWarStatusAsync(long chatId, WarService warService, CancellationToken ct)
    {
        var race = await warService.GetStatusAsync(ct);
        
        if (race?.Clans is null || race.Clans.Count == 0)
        {
            await _botClient.SendMessage(chatId, "Не удалось загрузить данные КВ.", cancellationToken: ct);
            return;
        }

        const int maxRiverRaceClans = 5;
        var ordered = race.Clans
            .Where(c => !string.IsNullOrWhiteSpace(c.Tag))
            .GroupBy(c => ClashRoyaleApiClient.NormalizeTag(c.Tag))
            .Select(g => g.OrderByDescending(x => x.PeriodPoints).ThenByDescending(x => x.Fame + x.RepairPoints).First())
            .OrderByDescending(x => x.PeriodPoints)
            .ThenByDescending(x => x.Fame + x.RepairPoints)
            .Take(maxRiverRaceClans)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("⚔️ Что с КВ?");
        sb.AppendLine($"Состояние: {race.State ?? "н/д"}, период: {race.PeriodType ?? "н/д"}");
        sb.AppendLine();

        for (var i = 0; i < ordered.Count; i++)
        {
            var clan = ordered[i];
            var medals = clan.PeriodPoints > 0
                ? clan.PeriodPoints
                : clan.Fame + clan.RepairPoints;
            var medalsNote = clan.PeriodPoints > 0
                ? "очки периода (медали)"
                : "слава + ремонт (очки)";

            sb.AppendLine($"{i + 1}. {clan.Name} {clan.Tag}");
            sb.AppendLine($"   {medalsNote}: {medals}");
            sb.AppendLine($"   Колод отыграно всего: {clan.DecksUsed}");
        }

        await SendTextChunksAsync(chatId, sb.ToString(), parseMode: ParseMode.None, ct);
    }

    private async Task SendTextChunksAsync(long chatId, string text, ParseMode parseMode, CancellationToken ct)
    {
        const int maxLen = 4096;
        if (text.Length <= maxLen)
        {
            await _botClient.SendMessage(chatId, text, parseMode: parseMode, cancellationToken: ct);
            return;
        }

        for (var offset = 0; offset < text.Length; offset += maxLen)
        {
            var len = Math.Min(maxLen, text.Length - offset);
            await _botClient.SendMessage(chatId, text.AsSpan(offset, len).ToString(), parseMode: parseMode, cancellationToken: ct);
        }
    }
    
    private async Task HandleBlacklistAsync(string playerTag, BotDbContext db, CancellationToken ct)
    {
        var tag = ClashRoyaleApiClient.NormalizeTag(playerTag);
        var existing = await db.BlacklistedPlayers.FirstOrDefaultAsync(x => x.PlayerTag == tag, ct);
        
        if (existing is null)
        {
            db.BlacklistedPlayers.Add(new BlacklistedPlayer { PlayerTag = tag });
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task HandleProfileAsync(Message message, ParsedCommand command, BotDbContext db, CancellationToken ct)
    {
        var tag = command.PlayerTag;
        
        if (tag is null && message.From is not null)
            tag = (await db.UserLinks.AsNoTracking().FirstOrDefaultAsync(x => x.TelegramUserId == message.From.Id, ct))?.PlayerTag;

        if (tag is null)
        {
            await _botClient.SendMessage(message.Chat.Id, "Профиль не подключён. Используй Профиль#ТЕГ.", cancellationToken: ct);
            return;
        }

        var player = await _api.GetPlayerAsync(tag, ct);
        
        if (player is null)
        {
            await _botClient.SendMessage(message.Chat.Id, "Не удалось загрузить профиль.", cancellationToken: ct);
            return;
        }

        string? clanRoleRu = null;
        
        if (player.Clan?.Tag is { Length: > 0 } clanTag)
        {
            var clan = await _api.GetClanAsync(clanTag, ct);
            var member = clan?.MemberList.FirstOrDefault(x => string.Equals(x.Tag, player.Tag, StringComparison.OrdinalIgnoreCase));
            
            if (member is not null)
                clanRoleRu = ToRuRole(member.Role);
        }
        
        clanRoleRu ??= !string.IsNullOrWhiteSpace(player.Role) ? ToRuRole(player.Role) : null;
        var wins = player.Wins;
        var losses = player.Losses;
        var total = Math.Max(player.BattleCount, wins + losses);
        var winRate = total > 0 ? (double)wins / total * 100.0 : 0.0;
        var badges = player.Badges;
        var bannerCount = GetBadgeProgress("BannerCollection");
        var emoteCount = GetBadgeProgress("EmoteCollection");
        var warWinsFromBadge = GetBadgeProgress("ClanWarWins");
        var accountDays = GetBadgeProgress("YearsPlayed");
        
        var sb = new StringBuilder();
        sb.AppendLine($"Информация об аккаунте {player.Name} {player.Tag}");
        sb.AppendLine();

        var clanName = player.Clan?.Name?.Name ?? player.Clan?.Name?.RawName;
        
        if (player.Clan is not null)
        {
            sb.AppendLine($"Клан: {clanName ?? "—"} {player.Clan.Tag}");
            
            if (!string.IsNullOrWhiteSpace(clanRoleRu))
                sb.AppendLine($"Роль: {clanRoleRu}");
            
            sb.AppendLine();
        }

        sb.AppendLine($"Трофеи: {player.Trophies}");
        sb.AppendLine($"Максимум трофеев: {player.BestTrophies}");
        sb.AppendLine($"Уровень короля: {player.ExpLevel}");
        
        if (accountDays > 0)
        {
            var ageText = FormatAccountAge(accountDays.Value);
            sb.AppendLine($"Возраст аккаунта: {ageText}");
        }
        else
        {
            sb.AppendLine("Возраст аккаунта: н/д");
        }
        
        sb.AppendLine();
        sb.AppendLine($"Победы: {wins} ({winRate:0.00}%)");
        sb.AppendLine($"Поражения: {losses}");
        sb.AppendLine($"Побед на 3 короны: {player.ThreeCrownWins}");
        sb.AppendLine($"Всего игр: {total}");
        sb.AppendLine();
        
        var warWinsTotal = warWinsFromBadge ?? player.WarDayWins;
        sb.AppendLine($"Побед в клановых войнах: {warWinsTotal}");
        sb.AppendLine($"Собрано баннеров: {bannerCount?.ToString() ?? "н/д"}");
        sb.AppendLine($"Собрано эмоций: {emoteCount?.ToString() ?? "н/д"}");
        sb.AppendLine($"Всего пожертвовано карт: {player.TotalDonations}");
        sb.AppendLine($"Старпоинты: {player.StarPoints}");
        sb.AppendLine();

        var cards = player.Cards;
        
        if (cards.Count > 0)
        {
            sb.AppendLine($"Открыто карт: {cards.Count}");
            sb.AppendLine("Количество прокачанных карт:");
            
            foreach (var level in cards.Select(GetDisplayCardLevel).Distinct().OrderByDescending(x => x))
                AppendCardLevelBreakdown(sb, cards, level);
        }
        else
        {
            sb.AppendLine("Открыто карт: н/д (API не вернул список cards)");
        }

        sb.AppendLine();

        if (player.Clan?.Tag is { Length: > 0 } warClanTag)
        {
            var warLog = await _api.GetClanWarLogAsync(warClanTag, 10, ct);
            
            if (warLog?.Items is { Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("История клановых войн:");
                sb.AppendLine($"{clanName ?? "—"} {player.Clan.Tag}");
                var warClanTagNorm = ClashRoyaleApiClient.NormalizeTag(warClanTag);

                foreach (var item in warLog.Items)
                {
                    var standing = item.Standings.FirstOrDefault(x =>
                        string.Equals(
                            ClashRoyaleApiClient.NormalizeTag(StandingTag(x)),
                            warClanTagNorm,
                            StringComparison.OrdinalIgnoreCase));
                    
                    if (standing is null)
                        continue;

                    sb.AppendLine($"{item.SeasonId}-{item.SectionIndex} 🏅{standing.PeriodPoints} ⚔️ {standing.DecksUsed}");
                    continue;

                    static string StandingTag(RiverRaceClanDto x) =>
                        !string.IsNullOrWhiteSpace(x.Tag) ? x.Tag : x.Clan?.Tag ?? string.Empty;
                }
            }
        }

        await _botClient.SendMessage(message.Chat.Id, sb.ToString().TrimEnd(), cancellationToken: ct);
        return;

        int? GetBadgeProgress(string name) =>
            badges.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))?.Progress;
    }

    private static void AppendCardLevelBreakdown(StringBuilder sb, List<PlayerCardDto> cards, int level)
    {
        var total = cards.Count;
        
        if (total == 0)
            return;
        var count = cards.Count(c => GetDisplayCardLevel(c) == level);
        
        if (count == 0)
            return;
        
        var pct = (double)count / total * 100.0;
        sb.AppendLine($"{level}лвл - {count} ({pct:0.00}%)");
    }

    private static int GetDisplayCardLevel(PlayerCardDto card)
    {
        var display = 16 - (card.MaxLevel - card.Level);

        return display switch
        {
            < 0 => 0,
            > 16 => 16,
            _ => display
        };
    }

    private static string FormatAccountAge(int days)
    {
        if (days <= 0)
            return "0д.";

        var years = days / 365;
        var rem = days % 365;
        var months = rem / 30;
        var d = rem % 30;
        var parts = new List<string>();
        
        if (years > 0)
            parts.Add($"{years}л.");
        
        if (months > 0)
            parts.Add($"{months}м.");
        
        if (d > 0 || parts.Count == 0)
            parts.Add($"{d}д.");

        return string.Join(" ", parts);
    }

    private static string ToRuRole(string? apiRole)
    {
        var r = (apiRole ?? string.Empty).Trim().ToLowerInvariant();
        
        return r switch
        {
            "leader" => "Глава",
            "coLeader" => "Соруководитель",
            "coleader" => "Соруководитель",
            "elder" => "Старейшина",
            "member" => "Участник",
            _ => apiRole ?? "—"
        };
    }
    
    private async Task HandleChestsAsync(Message message, ParsedCommand command, BotDbContext db, CancellationToken ct)
    {
        var tag = command.PlayerTag;
        
        if (tag is null && message.From is not null)
            tag = (await db.UserLinks.AsNoTracking().FirstOrDefaultAsync(x => x.TelegramUserId == message.From.Id, ct))?.PlayerTag;

        if (tag is null)
        {
            await _botClient.SendMessage(message.Chat.Id, "Сначала укажи тег: Сундуки#ТЕГ или подключи профиль.", cancellationToken: ct);
            return;
        }

        var player = await _api.GetPlayerAsync(tag, ct);
        
        if (player is null)
        {
            await _botClient.SendMessage(message.Chat.Id, "Не удалось загрузить профиль игрока для сундуков.", cancellationToken: ct);
            return;
        }

        var chests = await _api.GetUpcomingChestsAsync(tag, ct);
        
        if (chests is null)
        {
            await _botClient.SendMessage(message.Chat.Id, "Не удалось загрузить сундуки.", cancellationToken: ct);
            return;
        }

        var sb = new StringBuilder();
        var items = (chests.Items)
            .OrderBy(x => x.Index)
            .ToList();
        sb.AppendLine($"Сундуки игрока {player.Name} {player.Tag}");
        sb.AppendLine();

        var next = items.FirstOrDefault(x => x.Index == 0) ?? items.FirstOrDefault();
        var after = items.FirstOrDefault(x => x.Index == 1) ?? items.Skip(1).FirstOrDefault();

        if (next is not null)
            sb.AppendLine($"Следующий: {ChestDisplay(next)}");
        
        if (after is not null)
            sb.AppendLine($"Далее: {ChestDisplay(after)}");
        
        sb.AppendLine();
        sb.AppendLine("Затем:");

        var show = items
            .Where(x => x.Index is >= 2 and <= 10)
            .Concat(items.Where(x => x.Index is 14 or 37 or 92 or 688 or 689 or 690))
            .GroupBy(x => x.Index)
            .Select(g => g.First())
            .OrderBy(x => x.Index)
            .ToList();

        if (show.Count == 0)
            sb.AppendLine("—");
        else
            foreach (var x in show)
                sb.AppendLine($"{x.Index} - {ChestDisplay(x)}");

        var text = sb.ToString().TrimEnd();
        await _botClient.SendMessage(message.Chat.Id, text, cancellationToken: ct);
        return;

        string ChestDisplay(ChestItemDto x)
        {
            var name = (x.Name ?? "—").Trim();
            var hours = GetChestHours(name);
            
            return hours is null ? name : $"{name} ({hours}ч)";
        }
    }

    private static int? GetChestHours(string? apiChestName)
    {
        var n = (apiChestName ?? string.Empty).Trim().ToLowerInvariant();

        if (n.Contains("gold") || n.Contains("золот"))
            return 8;
        
        if (n.Contains("silver") || n.Contains("сереб"))
            return 3;
        
        if (n.Contains("giant") || n.Contains("огром"))
            return 12;
        
        if (n.Contains("magical") || n.Contains("магич"))
            return 12;
        
        if (n.Contains("epic") || n.Contains("эпич"))
            return 12;
        
        if (n.Contains("legendary") || n.Contains("леген"))
            return 24;
        
        if (n.Contains("mega lightning") || n.Contains("мегасундук") || n.Contains("молни"))
            return 24;
        
        if (n.Contains("lightning") || n.Contains("молни"))
            return 24;
        
        if (n.Contains("overflowing") || n.Contains("переполн"))
            return 12;
        
        if (n.Contains("fortune") || n.Contains("джокер"))
            return 24;
        
        if (n.Contains("tower troop") || n.Contains("башн"))
            return 0;
        
        if (n.Contains("gold crate") || n.Contains("ящик") || n.Contains("crate"))
            return 3;
        
        if (n.Contains("plentiful") || n.Contains("увесист"))
            return 8;

        return null;
    }

    private async Task HandleTopAsync(long chatId, int? requestedLimit, CancellationToken ct)
    {
        const int defaultLimit = 30;
        const int maxLimit = 100;
        var limit = requestedLimit is >= 1 and <= maxLimit ? requestedLimit.Value : defaultLimit;
        var rankings = await _api.GetClanRankingsAsync(_config.TopLocationId, limit, ct);
        
        if (rankings is null)
        {
            await _botClient.SendMessage(chatId, "Не удалось загрузить рейтинг кланов.", cancellationToken: ct);
            return;
        }

        var top = (rankings.Items).Take(limit).ToList();
        
        if (top.Count == 0)
        {
            await _botClient.SendMessage(chatId, "Рейтинг кланов пуст.", cancellationToken: ct);
            return;
        }

        var text = "Топ кланов РФ\n" + string.Join("\n", top.Select(x =>
        {
            var delta = x.PreviousRank - x.Rank;
            var trend = delta > 0 ? "⬆️" : delta < 0 ? "⬇️" : "➡️";
            var signed = delta < 0 ? $"{delta * -1}" : delta.ToString();
            
            return $"{x.Rank}. {x.Name} {x.Tag} 🏆{x.ClanScore} {trend}{signed}";
        }));
        
        await _botClient.SendMessage(chatId, text, cancellationToken: ct);
    }

    private async Task HandleInTopAsync(long chatId, CancellationToken ct)
    {
        var clan = await _api.GetClanAsync(_config.ClanTag, ct);
        
        if (clan is null)
        {
            await _botClient.SendMessage(chatId, "Не удалось загрузить данные клана.", cancellationToken: ct);
            return;
        }

        const int pageLimit = 100;
        const int maxPages = 15;
        var clanRankTask = FindClanInRankingAsync(
            after => _api.GetClanRankingsPageAsync(_config.TopLocationId, pageLimit, after, ct),
            _config.ClanTag,
            maxPages,
            ct);
        var warRankTask = FindClanInRankingAsync(
            after => _api.GetClanWarRankingsPageAsync(_config.TopLocationId, pageLimit, after, ct),
            _config.ClanTag,
            maxPages,
            ct);
        
        await Task.WhenAll(clanRankTask, warRankTask);

        var clanRank = clanRankTask.Result;
        var warRank = warRankTask.Result;
        var regionName = clanRank?.Location?.Name ?? warRank?.Location?.Name ?? "Russia";
        var sb = new StringBuilder();
        sb.AppendLine($"Клан: {clan.Name}");
        sb.AppendLine($"Трофеи: {clan.ClanWarTrophies}, счет: {clan.ClanScore}");
        sb.AppendLine($"Топ по кубкам в регионе {regionName} - {(clanRank?.Rank.ToString() ?? "н/д")}");
        sb.AppendLine($"Топ по КВ в регионе {regionName} - {(warRank?.Rank.ToString() ?? "н/д")}");

        await _botClient.SendMessage(chatId, sb.ToString().TrimEnd(), cancellationToken: ct);
    }

    private static async Task<RankedClanDto?> FindClanInRankingAsync(
        Func<string?, Task<ClanRankingDto?>> fetchPage,
        string clanTag,
        int maxPages,
        CancellationToken ct)
    {
        var clanTagNorm = ClashRoyaleApiClient.NormalizeTag(clanTag);
        string? after = null;

        for (var i = 0; i < maxPages; i++)
        {
            ct.ThrowIfCancellationRequested();

            var page = await fetchPage(after);
            var items = page?.Items ?? [];
            
            if (items.Count == 0)
                return null;

            var found = items.FirstOrDefault(x =>
                string.Equals(ClashRoyaleApiClient.NormalizeTag(x.Tag), clanTagNorm, StringComparison.OrdinalIgnoreCase));
            
            if (found is not null)
                return found;

            after = page?.Paging?.Cursors?.After;
            
            if (string.IsNullOrWhiteSpace(after))
                return null;
        }

        return null;
    }
    
    private async Task SetWarReminderEnabledAsync(BotDbContext db, bool enabled, CancellationToken ct)
    {
        var row = await db.BotSettings.FirstOrDefaultAsync(x => x.Key == "war_reminder_enabled", ct);
        
        if (row is null)
        {
            row = new BotSetting { Key = "war_reminder_enabled" };
            db.BotSettings.Add(row);
        }

        row.Value = enabled ? "1" : "0";
        await db.SaveChangesAsync(ct);
    }

    private async Task HandleChatMemberAsync(ChatMemberUpdated update, CancellationToken ct)
    {
        if (update.Chat.Id != _config.MainChatId)
            return;

        var leftStatuses = new[] { ChatMemberStatus.Left, ChatMemberStatus.Kicked };
        
        if (!leftStatuses.Contains(update.OldChatMember.Status) && leftStatuses.Contains(update.NewChatMember.Status))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            var link = await db.UserLinks.FirstOrDefaultAsync(x => x.TelegramUserId == update.NewChatMember.User.Id, ct);
            
            if (link is not null)
            {
                db.UserLinks.Remove(link);
                await db.SaveChangesAsync(ct);
            }
        }
    }
    
    private static bool TryHandleFunReply(string text, out string reply)
    {
        reply = string.Empty;
        var normalized = text.Trim().ToLowerInvariant();
        
        switch (normalized)
        {
            case "привет" or "даров" or "здравствуйте" or "ку" or "👋":
                reply = "Привет 👋 Я слежу за кланом и КВ.";
                return true;
            case "нет":
                reply = "Знаешь чей это ответ?)";
                return true;
            default:
                return false;
        }
    }

    private Task DenyAsync(long chatId, CancellationToken ct) =>
        _botClient.SendMessage(chatId, "⛔ Эта команда доступна только руководителям.", cancellationToken: ct);
}