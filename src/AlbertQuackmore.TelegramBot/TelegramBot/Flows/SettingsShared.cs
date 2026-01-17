using AlbertQuackmore.TelegramBot.TelegramBot.Utils;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace AlbertQuackmore.TelegramBot.TelegramBot.Flows;

public interface ISubFlow
{
    string SettingsMenuText { get; }
    string SettingsCallbackName { get; }
    string SettingsCallbackData { get; }

    Task StartFromSettingsRootAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken);
}
