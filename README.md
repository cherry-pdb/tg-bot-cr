# CrTelegramBot

Telegram-бот для клана **Clash Royale**: привязка аккаунтов к Telegram, профиль и сундуки через официальный API, уведомления руководителям, мониторинг состава клана, напоминания о клановой войне и региональный топ.

## Требования

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (для сборки и локального запуска)
- [PostgreSQL](https://www.postgresql.org/) (база данных; в продакшене удобно поднимать в Docker)
- Токен бота у [@BotFather](https://t.me/BotFather)
- Ключ [Clash Royale API](https://developer.clashroyale.com/)

## Структура решения

- `CrTelegramBot/` — worker-приложение (`Microsoft.NET.Sdk.Worker`): long polling Telegram, фоновые задачи.
- `CrTelegramBot/Data` — Entity Framework Core, `BotDbContext`, сущности (привязки пользователей, ЧС, снимки клана и т.д.).
- `CrTelegramBot/Telegram` — обработка сообщений и команд.
- `CrTelegramBot/ClashRoyale` — HTTP-клиент к `https://api.clashroyale.com/v1/`.
- `CrTelegramBot/Workers` — монитор клана, напоминания о КВ.

## Конфигурация

Настройки читаются из `appsettings.json` / `appsettings.Development.json` и **переопределяются** переменными окружения (в Docker — через `environment` в `docker-compose`).

### Строка подключения к PostgreSQL

Ключ конфигурации: `ConnectionStrings:DefaultConnection`.

Пример:

```text
Host=localhost;Port=5432;Database=DbName;Username=pg_username;Password=pg_pass
```

Переменная окружения:

```text
ConnectionStrings__DefaultConnection=...
```

### Секция `BotConfig`

| Параметр | Описание |
|----------|----------|
| `TelegramBotToken` | Токен Telegram-бота |
| `ClashRoyaleApiToken` | Bearer-токен Clash Royale API |
| `MainChatId` | ID основного чата клана (для событий участников и напоминаний) |
| `ClanTag` | Тег клана в формате `#XXXX` |
| `LeaderUsernames` | Telegram `@username` руководителей (без `@`) |
| `ReminderHourUtc` / `WarEndHourUtc` | Часы UTC для напоминаний о КВ |
| `WarEndDaySummaryMinutesBeforeEnd` | (Legacy) Сводка по КВ за N минут до `WarEndHourUtc` (UTC). `0` — отключить |
| `WarEndDaySummaryTimeZoneId` | Таймзона (Windows ID) для расписания сводки по локальному времени. Для МСК обычно `Russian Standard Time` |
| `WarEndDaySummaryLocalTime` | Локальное время `HH:mm` для авто-сводки. Если задано — **имеет приоритет** над `WarEndDaySummaryMinutesBeforeEnd` |
| `WarEndDaySummaryDaysOfWeek` | Дни недели для авто-сводки: `Monday..Sunday` (например `["Friday","Saturday","Sunday","Monday"]`). Пусто — каждый день |
| `WarNudgesHoursBeforeEnd` | За сколько часов до конца дня КВ слать пинги |
| `TopLocationId` | ID локации для команд «Топ» / «Втопе» (по умолчанию регион РФ) |
| `ClanMonitorIntervalSeconds` | Период опроса API для мониторинга состава клана |

### Генерация `.env` для Docker

Из корня репозитория (рядом с будущим `docker-compose.yml`):

## Локальный запуск

```bash
docker-compose up --build -d
```

Имя хоста в строке подключения должно совпадать с именем сервиса Postgres в `docker-compose` (например `Host=postgres`).

После `docker compose down` данные Postgres **сохраняются**, если используется именованный volume и не указан флаг `-v`.

## Команды бота

Список и описания выводятся по сообщению **«Команды»**. В числе возможностей: профиль и сундуки по тегу или привязке, топ кланов, привязка/отвязка аккаунта для руководителей, участники, статус КВ, чёрный список в ЛС и др. Точный синтаксис см. в `Telegram/CommandParser.cs` и справке в `BotUpdateHandler`.