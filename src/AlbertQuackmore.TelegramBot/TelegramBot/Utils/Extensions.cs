using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AlbertQuackmore.TelegramBot.TelegramBot.Utils;

public static class Extensions
{
    /// <summary>
    /// Sends a new message for a sub-flow and tracks it for later deletion.
    /// Updates the state's LastBotMessageId and adds the message to FlowMessageIds.
    /// </summary>
    public static async Task<Message> SendFlowMessageAsync(
        this ITelegramBotClient botClient,
        ChatId chatId,
        ConversationState state,
        string text,
        ParseMode parseMode = ParseMode.Markdown,
        IReplyMarkup? replyMarkup = default,
        CancellationToken cancellationToken = default)
    {
        var message = await botClient.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: parseMode,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);

        state.LastBotMessageId = message.MessageId;
        state.TrackFlowMessage(message.MessageId);

        return message;
    }

    /// <summary>
    /// Edits an existing flow message or sends a new one if editing fails.
    /// Tracks the message for later deletion.
    /// </summary>
    public static async Task<Message> TryEditOrSendFlowMessageAsync(
        this ITelegramBotClient botClient,
        ChatId chatId,
        ConversationState state,
        string text,
        ParseMode parseMode = ParseMode.Markdown,
        InlineKeyboardMarkup? replyMarkup = default,
        CancellationToken cancellationToken = default)
    {
        // If we have a LastBotMessageId that's different from MainMenuMessageId, try to edit it
        if (state.LastBotMessageId != null && state.LastBotMessageId != state.MainMenuMessageId)
        {
            try
            {
                return await botClient.EditMessageText(
                    chatId: chatId,
                    messageId: state.LastBotMessageId.Value,
                    text: text,
                    parseMode: parseMode,
                    replyMarkup: replyMarkup,
                    cancellationToken: cancellationToken);
            }
            catch (ApiRequestException)
            {
                // If editing fails, send a new message
            }
        }

        // Send a new message and track it
        return await botClient.SendFlowMessageAsync(chatId, state, text, parseMode, replyMarkup, cancellationToken);
    }
}