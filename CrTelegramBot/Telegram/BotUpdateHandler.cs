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
    private readonly AutoDeleteService _autoDelete;

    public BotUpdateHandler(
        ITelegramBotClient botClient,
        IServiceScopeFactory scopeFactory,
        ClashRoyaleApiClient api,
        BotConfig config,
        CommandParser parser,
        AutoDeleteService autoDelete,
        ILogger<BotUpdateHandler> logger)
    {
        _botClient = botClient;
        _scopeFactory = scopeFactory;
        _api = api;
        _config = config;
        _parser = parser;
        _autoDelete = autoDelete;
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
                
                await SendBotMessageAsync(
                    mes.Chat.Id,
                    mes.Chat.Type,
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
            await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, funReply, cancellationToken: ct);
            return;
        }

        var command = _parser.Parse(text);
        
        if (command is null)
            return;

        var isLeader = await leaderService.IsLeaderAsync(message.From, ct);
        
        switch (command.Kind)
        {
            case ParsedCommandKind.AddLeader:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                await HandleAddLeaderAsync(message, command, leaderService, ct);
                break;

            case ParsedCommandKind.RemoveLeader:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                await HandleRemoveLeaderAsync(message, command, leaderService, ct);
                break;

            case ParsedCommandKind.Leaders:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                await HandleLeadersAsync(message.Chat.Id, message.Chat.Type, leaderService, ct);
                break;

            case ParsedCommandKind.Connect:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                await HandleConnectAsync(message, command, userLinkService, ct);
                break;

            case ParsedCommandKind.Disconnect:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                await HandleDisconnectAsync(message, command, db, userLinkService, ct);
                break;

            case ParsedCommandKind.EnableNotifications:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                await notificationService.SetLeaderNotificationsAsync(message.From!.Id, true, ct);
                await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, "✅ Личные уведомления включены.", cancellationToken: ct);
                break;

            case ParsedCommandKind.DisableNotifications:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                await notificationService.SetLeaderNotificationsAsync(message.From!.Id, false, ct);
                await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, "✅ Личные уведомления отключены.", cancellationToken: ct);
                break;

            case ParsedCommandKind.Commands:
                await SendBotMessageAsync(
                    message.Chat.Id,
                    message.Chat.Type,
                    BuildCommandsHelpDetailed(isLeader, message.Chat.Type),
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);
                break;

            case ParsedCommandKind.Participants:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                await HandleParticipantsAsync(message.Chat.Id, message.Chat.Type, clanService, db, ct);
                break;

            case ParsedCommandKind.WarStatus:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                await HandleWarStatusAsync(message.Chat.Id, message.Chat.Type, warService, ct);
                break;

            case ParsedCommandKind.RemindWar:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                await SetWarReminderEnabledAsync(db, true, ct);
                await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, "✅ Напоминания о КВ включены.", cancellationToken: ct);
                break;

            case ParsedCommandKind.Blacklist:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                {
                    var tag = await ResolveBlackListPlayerTagAsync(message, command, db, ct);
                    if (tag is null)
                    {
                        await SendBotMessageAsync(
                            message.Chat.Id,
                            message.Chat.Type,
                            "Не удалось определить тег: укажи В ЧС#ТЕГ, ответь на сообщение пользователя или В ЧС@username. Если у человека несколько привязок — только В ЧС#ТЕГ.",
                            cancellationToken: ct);
                        break;
                    }

                    await HandleBlacklistAsync(tag, db, ct);
                    await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, $"⛔ Игрок {tag} добавлен в ЧС.", cancellationToken: ct);
                }
                break;

            case ParsedCommandKind.RemoveBlacklist:
                if (!isLeader) { await DenyAsync(message.Chat.Id, message.Chat.Type, ct); return; }
                {
                    var tag = await ResolveBlackListPlayerTagAsync(message, command, db, ct);
                    if (tag is null)
                    {
                        await SendBotMessageAsync(
                            message.Chat.Id,
                            message.Chat.Type,
                            "Не удалось определить тег: укажи Из ЧС#ТЕГ, ответь на сообщение пользователя или Из ЧС@username. Если у человека несколько привязок — только Из ЧС#ТЕГ.",
                            cancellationToken: ct);
                        break;
                    }

                    var removed = await HandleRemoveBlacklistAsync(tag, db, ct);
                    await SendBotMessageAsync(
                        message.Chat.Id,
                        message.Chat.Type,
                        removed
                            ? $"✅ Игрок {tag} убран из ЧС."
                            : $"ℹ️ Игрок {tag} не был в ЧС.",
                        cancellationToken: ct);
                }
                break;

            case ParsedCommandKind.Profile:
                await HandleProfileAsync(message, command, db, ct);
                break;

            case ParsedCommandKind.Top:
                await HandleTopAsync(message.Chat.Id, message.Chat.Type, command.TopLimit, ct);
                break;
            case ParsedCommandKind.InTop:
                await HandleInTopAsync(message.Chat.Id, message.Chat.Type, ct);
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
        sb.AppendLine("- В ЧС#ТЕГ");
        sb.AppendLine("- Добавить руководителя (ответом) / Добавить руководителя@username");
        sb.AppendLine("- Удалить руководителя (ответом) / Удалить руководителя@username");
        sb.AppendLine("- Руководители");

        return sb.ToString().TrimEnd();
    }
    
    private static string BuildCommandsHelpDetailed(bool isLeader, ChatType chatType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("📌 Доступные команды (подробно)");
        sb.AppendLine();
        sb.AppendLine("Для всех:");
        sb.AppendLine("• <b><u>Профиль</u></b> — показать профиль(и) всех привязанных аккаунтов (до 5); если нужен один — <b>Профиль#ТЕГ</b>.");
        sb.AppendLine("• <b><u>Профиль#ТЕГ</u></b> — показать профиль игрока по тегу (можно без своей привязки).");
        sb.AppendLine("• <b><u>Топ / ТопN</u></b> — показать топ N кланов по кубкам в регионе (по умолчанию топ30).");
        sb.AppendLine("• <b><u>Втопе</u></b> — показать место клана в топе по кубкам и по КВ.");
        sb.AppendLine("• <b><u>Команды</u></b> — краткий список команд и их описание.");
        sb.AppendLine();
        sb.AppendLine("Только для руководителей:");
        sb.AppendLine("• <b><u>Подключить#ТЕГ (ответом на сообщение)</u></b> — привязать ещё один аккаунт Clash Royale к этому Telegram (у одного человека может быть несколько тегов).");
        sb.AppendLine("• <b><u>Подключить#ТЕГ@username</u></b> — то же по @username.");
        sb.AppendLine("• <b><u>Отключить#ТЕГ</u></b> — снять привязку с конкретного тега.");
        sb.AppendLine("• <b><u>Отключить#ТЕГ@username</u></b> — снять привязку по нику (если у пользователя один тег; иначе укажи тег).");
        sb.AppendLine("• <b><u>Включить уведомления</u></b> — включить личные уведомления о входе/выходе игроков и напоминаниях.");
        sb.AppendLine("• <b><u>Отключить уведомления</u></b> — отключить личные уведомления для руководителя.");
        sb.AppendLine("• <b><u>Участники</u></b> — список участников клана с отметкой, кто привязан к Telegram.");
        sb.AppendLine("• <b><u>Что с КВ</u></b> — текущий статус КВ и кланы в рейсе.");
        sb.AppendLine("• <b><u>Напомни о КВ</u></b> — включить автоматические напоминания о КВ в основной чат.");
        sb.AppendLine("• <b><u>В ЧС#ТЕГ</u></b> — добавить в чёрный список по тегу; также <b>В ЧС</b> ответом или <b>В ЧС@username</b>, если ровно одна привязка.");
        sb.AppendLine("• <b><u>Из ЧС#ТЕГ</u></b> — убрать из чёрного списка; при нескольких привязках — только с тегом.");
        sb.AppendLine("• <b><u>Добавить руководителя</u></b> — добавить руководителя (ответом на сообщение) или <b>Добавить руководителя@username</b>.");
        sb.AppendLine("• <b><u>Удалить руководителя</u></b> — удалить руководителя (ответом на сообщение) или <b>Удалить руководителя@username</b>.");
        sb.AppendLine("• <b><u>Руководители</u></b> — показать текущий список руководителей (из настроек бота).");

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
            await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, "Игрок не найден в Clash Royale API.", cancellationToken: ct);
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
                await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, $"Не вижу пользователя @{username} в истории сообщений. Пусть он напишет в чат, либо подключай ответом на сообщение.", cancellationToken: ct);
                return;
            }

            await userLinkService.UpsertAsync(seen.TelegramUserId, seen.TelegramUsername, seen.TelegramDisplayName, command.PlayerTag!, ct);
        }
        else
        {
            await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, "Используй: ответом на сообщение 'Подключить#ТЕГ' или в чат 'Подключить#ТЕГ@username'.", cancellationToken: ct);
            return;
        }

        await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, $"✅ Подключено: {player.Name} {player.Tag}", cancellationToken: ct);
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
            {
                var links = await db.UserLinks.Where(x => x.TelegramUserId == seen.TelegramUserId).ToListAsync(ct);
                link = links.Count switch
                {
                    0 => null,
                    1 => links[0],
                    _ => null
                };

                if (link is null && links.Count > 1)
                {
                    await SendBotMessageAsync(
                        message.Chat.Id,
                        message.Chat.Type,
                        $"Несколько привязок у @{username}: укажи Отключить#ТЕГ. Теги: {string.Join(", ", links.Select(x => x.PlayerTag))}",
                        cancellationToken: ct);
                    return;
                }
            }
        }

        if (link is null && message.ReplyToMessage?.From is { } replyFrom)
        {
            var links = await db.UserLinks.Where(x => x.TelegramUserId == replyFrom.Id).ToListAsync(ct);
            link = links.Count switch
            {
                0 => null,
                1 => links[0],
                _ => null
            };

            if (link is null && links.Count > 1)
            {
                await SendBotMessageAsync(
                    message.Chat.Id,
                    message.Chat.Type,
                    $"Несколько привязок: укажи Отключить#ТЕГ. Теги: {string.Join(", ", links.Select(x => x.PlayerTag))}",
                    cancellationToken: ct);
                return;
            }
        }

        if (link is null)
        {
            await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, "Связка не найдена.", cancellationToken: ct);
            return;
        }

        await userLinkService.RemoveAsync(link, ct);
        await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, $"✅ Отключено: {link.TelegramDisplayName} ← {link.PlayerTag}", cancellationToken: ct);
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

    private async Task HandleParticipantsAsync(long chatId, ChatType chatType, ClanService clanService, BotDbContext db, CancellationToken ct)
    {
        var clan = await clanService.GetClanAsync(ct);

        if (clan is null)
        {
            await SendBotMessageAsync(chatId, chatType, "Не удалось загрузить состав клана.", cancellationToken: ct);
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

        await SendBotMessageAsync(
            chatId,
            chatType,
            "👥 Участники клана\n\n" + string.Join("\n", lines.Take(50)),
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }
    
    private async Task HandleWarStatusAsync(long chatId, ChatType chatType, WarService warService, CancellationToken ct)
    {
        var race = await warService.GetStatusAsync(ct);
        
        if (race?.Clans is null || race.Clans.Count == 0)
        {
            await SendBotMessageAsync(chatId, chatType, "Не удалось загрузить данные КВ.", cancellationToken: ct);
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
        sb.AppendLine($"Период: {race.PeriodType ?? "н/д"}");
        sb.AppendLine();

        for (var i = 0; i < ordered.Count; i++)
        {
            var clan = ordered[i];
            var medals = clan.PeriodPoints > 0
                ? clan.PeriodPoints
                : clan.Fame + clan.RepairPoints;
            var medalsNote = clan.PeriodPoints > 0
                ? "Медали периода"
                : "Слава + ремонт (очки)";
            var sumDecks = clan.Participants.Sum(p => p.DecksUsedToday);
            var avgMedals = (double)medals / sumDecks;
            var unplayedPlayers = clan.Participants.Where(p => p.DecksUsedToday is > 0 and < 4);
            var unusedDecks = unplayedPlayers.Sum(p => p.DecksUsedToday);

            var nameEsc = System.Net.WebUtility.HtmlEncode(clan.Name);
            var tagEsc = System.Net.WebUtility.HtmlEncode(clan.Tag);
            sb.AppendLine($"{i + 1}. <b>{nameEsc} {tagEsc}</b>");
            sb.AppendLine($"   {System.Net.WebUtility.HtmlEncode(medalsNote)}: {medals}");
            sb.AppendLine($"   Колод отыграно всего: {sumDecks}/200");
            sb.AppendLine($"   Среднее кол-во медалей за игру: {avgMedals.ToString("F2")}");
            sb.AppendLine($"   Максимально клан может набрать: {medals + unusedDecks * 200}");
            sb.AppendLine($"   Кол-во недоигранных колод: {unusedDecks}");
            sb.AppendLine($"   Кол-во человек недоиграли: {unplayedPlayers.Count()}");
            sb.AppendLine();
        }

        await SendTextChunksAsync(chatId, chatType, sb.ToString(), parseMode: ParseMode.Html, ct);
    }

    private async Task SendTextChunksAsync(long chatId, ChatType chatType, string text, ParseMode? parseMode = null, CancellationToken ct = default)
    {
        const int maxLen = 4096;
        if (text.Length <= maxLen)
        {
            await SendBotMessageAsync(chatId, chatType, text, parseMode: parseMode, cancellationToken: ct);
            return;
        }

        for (var offset = 0; offset < text.Length; offset += maxLen)
        {
            var len = Math.Min(maxLen, text.Length - offset);
            await SendBotMessageAsync(chatId, chatType, text.AsSpan(offset, len).ToString(), parseMode: parseMode, cancellationToken: ct);
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

    private async Task<bool> HandleRemoveBlacklistAsync(string playerTag, BotDbContext db, CancellationToken ct)
    {
        var tag = ClashRoyaleApiClient.NormalizeTag(playerTag);
        var existing = await db.BlacklistedPlayers.FirstOrDefaultAsync(x => x.PlayerTag == tag, ct);

        if (existing is null)
            return false;

        db.BlacklistedPlayers.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<string?> ResolveBlackListPlayerTagAsync(Message message, ParsedCommand command, BotDbContext db, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(command.PlayerTag))
            return ClashRoyaleApiClient.NormalizeTag(command.PlayerTag);

        if (!string.IsNullOrWhiteSpace(command.TelegramUsername))
        {
            var username = command.TelegramUsername.Trim().TrimStart('@');
            var usernameLower = username.ToLowerInvariant();
            var seenCandidates = await db.ChatUsers.AsNoTracking()
                .Where(x => x.TelegramUsername != "" && x.TelegramUsername.ToLower() == usernameLower)
                .ToListAsync(ct);
            var seen = seenCandidates
                .OrderByDescending(x => x.LastSeenAt)
                .FirstOrDefault();

            if (seen is null)
                return null;

            var links = await db.UserLinks.AsNoTracking().Where(x => x.TelegramUserId == seen.TelegramUserId).ToListAsync(ct);
            if (links.Count != 1)
                return null;

            return ClashRoyaleApiClient.NormalizeTag(links[0].PlayerTag);
        }

        if (message.ReplyToMessage?.From is { } from)
        {
            var links = await db.UserLinks.AsNoTracking().Where(x => x.TelegramUserId == from.Id).ToListAsync(ct);
            if (links.Count != 1)
                return null;

            return ClashRoyaleApiClient.NormalizeTag(links[0].PlayerTag);
        }

        return null;
    }

    private async Task HandleProfileAsync(Message message, ParsedCommand command, BotDbContext db, CancellationToken ct)
    {
        List<string> tags;

        if (!string.IsNullOrWhiteSpace(command.PlayerTag))
        {
            tags = new List<string> { ClashRoyaleApiClient.NormalizeTag(command.PlayerTag!) };
        }
        else if (message.From is not null)
        {
            tags = await db.UserLinks.AsNoTracking()
                .Where(x => x.TelegramUserId == message.From.Id)
                .OrderBy(x => x.Id)
                .Select(x => x.PlayerTag)
                .ToListAsync(ct);
        }
        else
        {
            await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, "Профиль не подключён. Используй Профиль#ТЕГ.", cancellationToken: ct);
            return;
        }

        if (tags.Count == 0)
        {
            await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, "Профиль не подключён. Используй Профиль#ТЕГ.", cancellationToken: ct);
            return;
        }

        const int maxAccounts = 5;

        if (tags.Count > maxAccounts)
        {
            await SendBotMessageAsync(
                message.Chat.Id,
                message.Chat.Type,
                $"Слишком много привязанных аккаунтов ({tags.Count}). Покажи профиль по одному: Профиль#ТЕГ.",
                cancellationToken: ct);
            return;
        }

        var sb = new StringBuilder();

        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            var player = await _api.GetPlayerAsync(tag, ct);

            if (player is null)
            {
                sb.AppendLine($"Не удалось загрузить профиль для {tag}.");

                if (i < tags.Count - 1)
                    sb.AppendLine();

                continue;
            }

            if (tags.Count > 1)
            {
                if (i > 0)
                    sb.AppendLine().AppendLine("──────────").AppendLine();

                sb.AppendLine($"Аккаунт {i + 1}/{tags.Count}");
                sb.AppendLine();
            }

            sb.Append(await FormatPlayerProfileSectionAsync(player, ct));

            if (i < tags.Count - 1)
                sb.AppendLine();
        }

        await SendTextChunksAsync(message.Chat.Id, message.Chat.Type, sb.ToString().TrimEnd(), parseMode: null, ct);
    }

    private async Task<string> FormatPlayerProfileSectionAsync(PlayerDto player, CancellationToken ct)
    {
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

        return sb.ToString().TrimEnd();

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

    private async Task HandleTopAsync(long chatId, ChatType chatType, int? requestedLimit, CancellationToken ct)
    {
        const int defaultLimit = 30;
        const int maxLimit = 100;
        var limit = requestedLimit is >= 1 and <= maxLimit ? requestedLimit.Value : defaultLimit;
        var rankings = await _api.GetClanRankingsAsync(_config.TopLocationId, limit, ct);
        
        if (rankings is null)
        {
            await SendBotMessageAsync(chatId, chatType, "Не удалось загрузить рейтинг кланов.", cancellationToken: ct);
            return;
        }

        var top = (rankings.Items).Take(limit).ToList();
        
        if (top.Count == 0)
        {
            await SendBotMessageAsync(chatId, chatType, "Рейтинг кланов пуст.", cancellationToken: ct);
            return;
        }

        var text = "Топ кланов РФ\n" + string.Join("\n", top.Select(x =>
        {
            var delta = x.PreviousRank - x.Rank;
            var trend = delta > 0 ? "⬆️" : delta < 0 ? "⬇️" : "➡️";
            var signed = delta < 0 ? $"{delta * -1}" : delta.ToString();
            
            return $"{x.Rank}. {x.Name} {x.Tag} 🏆{x.ClanScore} {trend}{signed}";
        }));
        
        await SendBotMessageAsync(chatId, chatType, text, cancellationToken: ct);
    }

    private async Task HandleInTopAsync(long chatId, ChatType chatType, CancellationToken ct)
    {
        var clan = await _api.GetClanAsync(_config.ClanTag, ct);
        
        if (clan is null)
        {
            await SendBotMessageAsync(chatId, chatType, "Не удалось загрузить данные клана.", cancellationToken: ct);
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

        await SendBotMessageAsync(chatId, chatType, sb.ToString().TrimEnd(), cancellationToken: ct);
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
            var links = await db.UserLinks.Where(x => x.TelegramUserId == update.NewChatMember.User.Id).ToListAsync(ct);

            if (links.Count > 0)
            {
                db.UserLinks.RemoveRange(links);
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

    private Task DenyAsync(long chatId, ChatType chatType, CancellationToken ct) =>
        SendBotMessageAsync(chatId, chatType, "⛔ Эта команда доступна только руководителям.", cancellationToken: ct);

    private async Task SendBotMessageAsync(long chatId, ChatType chatType, string text, ParseMode? parseMode = null, CancellationToken cancellationToken = default)
    {
        var sent = parseMode is { } mode
            ? await _botClient.SendMessage(chatId, text, parseMode: mode, cancellationToken: cancellationToken)
            : await _botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);

        if (chatType != ChatType.Private)
            _autoDelete.ScheduleDelete(chatId, sent.MessageId, TimeSpan.FromHours(1));
    }

    private async Task HandleLeadersAsync(long chatId, ChatType chatType, LeaderService leaderService, CancellationToken ct)
    {
        var leaders = await leaderService.GetLeadersAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("👑 Руководители (настройка бота)");
        sb.AppendLine();

        if (leaders.UserIds.Length == 0 && leaders.Usernames.Length == 0)
        {
            sb.AppendLine("— список пуст —");
            await SendBotMessageAsync(chatId, chatType, sb.ToString().TrimEnd(), cancellationToken: ct);
            return;
        }

        if (leaders.Usernames.Length > 0)
        {
            sb.AppendLine("По @username:");
            foreach (var u in leaders.Usernames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"- @{u}");
            sb.AppendLine();
        }

        if (leaders.UserIds.Length > 0)
        {
            sb.AppendLine("По Telegram ID:");
            foreach (var id in leaders.UserIds.OrderBy(x => x))
                sb.AppendLine($"- {id}");
        }

        await SendBotMessageAsync(chatId, chatType, sb.ToString().TrimEnd(), cancellationToken: ct);
    }

    private async Task HandleAddLeaderAsync(Message message, ParsedCommand command, LeaderService leaderService, CancellationToken ct)
    {
        var target = message.ReplyToMessage?.From;
        var targetId = target?.Id;
        var targetUsername = target?.Username;

        if (!string.IsNullOrWhiteSpace(command.TelegramUsername))
            targetUsername = command.TelegramUsername;

        if (targetId is null && string.IsNullOrWhiteSpace(targetUsername))
        {
            await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, "Используй: ответом на сообщение «Добавить руководителя» или «Добавить руководителя@username».", cancellationToken: ct);
            return;
        }

        var (changed, current) = await leaderService.AddLeaderAsync(targetId, targetUsername, ct);
        var who = !string.IsNullOrWhiteSpace(targetUsername) ? $"@{targetUsername.Trim().TrimStart('@')}" : targetId?.ToString() ?? "—";
        var text = changed
            ? $"✅ Добавлен руководитель: {who}\n\nВсего: {current.Usernames.Length} @username, {current.UserIds.Length} ID."
            : $"ℹ️ Уже есть в списке руководителей: {who}";

        await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, text, cancellationToken: ct);
    }

    private async Task HandleRemoveLeaderAsync(Message message, ParsedCommand command, LeaderService leaderService, CancellationToken ct)
    {
        var target = message.ReplyToMessage?.From;
        var targetId = target?.Id;
        var targetUsername = target?.Username;

        if (!string.IsNullOrWhiteSpace(command.TelegramUsername))
            targetUsername = command.TelegramUsername;

        if (targetId is null && string.IsNullOrWhiteSpace(targetUsername))
        {
            await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, "Используй: ответом на сообщение «Удалить руководителя» или «Удалить руководителя@username».", cancellationToken: ct);
            return;
        }

        var (changed, current) = await leaderService.RemoveLeaderAsync(targetId, targetUsername, ct);
        var who = !string.IsNullOrWhiteSpace(targetUsername) ? $"@{targetUsername.Trim().TrimStart('@')}" : targetId?.ToString() ?? "—";
        var text = changed
            ? $"✅ Удалён руководитель: {who}\n\nОсталось: {current.Usernames.Length} @username, {current.UserIds.Length} ID."
            : $"ℹ️ Не найден в списке руководителей: {who}";

        await SendBotMessageAsync(message.Chat.Id, message.Chat.Type, text, cancellationToken: ct);
    }
}