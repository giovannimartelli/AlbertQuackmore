using ExpenseTracker.TelegramBot.TelegramBot.Utils;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ExpenseTracker.TelegramBot.TelegramBot.Flows;

public interface ISubFlow
{
    string SettingsMenuText { get; }
    string SettingsCallbackName { get; }
    string SettingsCallbackData { get; }
    string SettingsEntryStep { get; }
    Task StartFromSettingsRootAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken);
}
