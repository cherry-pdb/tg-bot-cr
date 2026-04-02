using Telegram.Bot;

namespace CrTelegramBot.Services;

public sealed class AutoDeleteService
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<AutoDeleteService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public AutoDeleteService(ITelegramBotClient botClient, ILogger<AutoDeleteService> logger, IHostApplicationLifetime lifetime)
    {
        _botClient = botClient;
        _logger = logger;
        _lifetime = lifetime;
    }

    public void ScheduleDelete(long chatId, int messageId, TimeSpan delay)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, _lifetime.ApplicationStopping);
                await _botClient.DeleteMessage(chatId, messageId, cancellationToken: _lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not auto-delete message {MessageId} in chat {ChatId}", messageId, chatId);
            }
        });
    }
}

