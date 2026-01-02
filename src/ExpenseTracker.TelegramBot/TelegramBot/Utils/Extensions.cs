using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExpenseTracker.TelegramBot.TelegramBot.Utils;

public static class Extensions
{
    public static async Task<Message> TryEditMessageText(this ITelegramBotClient botClient,
        ChatId chatId,
        int? messageId,
        string text,
        ParseMode parseMode = default,
        InlineKeyboardMarkup? replyMarkup = default,
        CancellationToken cancellationToken = default
    )
    {
        if (messageId != null)
            try
            {
                return await botClient.EditMessageText(
                    chatId: chatId,
                    messageId: messageId.Value,
                    text: text,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: replyMarkup,
                    cancellationToken: cancellationToken);
            }
            catch (ApiRequestException)
            {
            }

        return await botClient.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }
}