using System.Collections.Concurrent;
using ExpenseTracker.TelegramBot.TelegramBot.Flows;
using ExpenseTracker.TelegramBot.TelegramBot.Utils;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ExpenseTracker.TelegramBot.TelegramBot;

/// <summary>
/// Background service that handles Telegram bot polling and delegates to FlowController.
/// </summary>
public class BotService(
    ITelegramBotClient botClient,
    FlowController flowController,
    IOptions<TelegramOptions> options,
    ILogger<BotService> logger)
    : BackgroundService
{
    private readonly TelegramOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, ConversationState> _conversationStates = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
            DropPendingUpdates = true
        };

        logger.LogInformation("Starting Telegram bot polling (with FlowController)...");

        await botClient.ReceiveAsync(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);
    }

    private ConversationState GetOrCreateState(string chatUsername)
    {
        return _conversationStates.GetOrAdd(chatUsername, _ => new ConversationState());
    }

    private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.CallbackQuery is { } callbackQuery)
            {
                await HandleCallbackQueryAsync(client, callbackQuery, cancellationToken);
            }
            else if (update.Message is { Text: not null } message)
            {
                await HandleTextMessageAsync(client, message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling update {UpdateId}", update.Id);
        }
    }

    private async Task HandleTextMessageAsync(
        ITelegramBotClient client,
        Message message,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;
        var chatUsername = chat.Username;

        if (chatUsername is null || !IsAuthorized(chatUsername))
        {
            logger.LogWarning("Unauthorized access attempt from chat {ChatId}", chatUsername ?? "unknown");
            await client.SendMessage(
                chatId: chat.Id,
                text: "Non sei autorizzato ad usare questo bot.",
                cancellationToken: cancellationToken);
            return;
        }

        logger.LogInformation("Received message from {ChatId}: {Message}", chatUsername, message.Text);

        var state = GetOrCreateState(chatUsername);


        await flowController.HandleTextMessageAsync(client, message, state, cancellationToken);

        // Delete user message after processing
        await TryDeleteMessageAsync(client, chat.Id, message.MessageId, cancellationToken);
    }

    private async Task HandleCallbackQueryAsync(
        ITelegramBotClient client,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        var chat = callbackQuery.Message?.Chat;
        var chatUsername = chat?.Username;
        var callbackData = callbackQuery.Data;

        if (chat is null || chatUsername is null || callbackData is null)
            return;

        if (!IsAuthorized(chatUsername))
        {
            await client.AnswerCallbackQuery(callbackQuery.Id, "Non autorizzato", cancellationToken: cancellationToken);
            return;
        }

        logger.LogInformation("Received callback from {ChatId}: {Data}", chatUsername, callbackData);

        var state = GetOrCreateState(chatUsername);


        await flowController.HandleCallbackQueryAsync(client, callbackQuery, state, cancellationToken);
    }

    private bool IsAuthorized(string chatUsername)
    {
        return _options.AllowedUsername.Length == 0 || _options.AllowedUsername.Contains(chatUsername);
    }

    private async Task TryDeleteMessageAsync(ITelegramBotClient client, long chatId, int messageId, CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteMessage(chatId, messageId, cancellationToken);
        }
        catch (ApiRequestException ex)
        {
            logger.LogDebug("Could not delete message {MessageId}: {Error}", messageId, ex.Message);
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error [{apiRequestException.ErrorCode}]: {apiRequestException.Message}",
            _ => exception.ToString()
        };

        logger.LogError(exception, "Telegram polling error: {ErrorMessage}", errorMessage);

        if (exception is not ApiRequestException)
        {
            logger.LogInformation("Waiting 5 seconds before retrying...");
            return Task.Delay(5000, cancellationToken);
        }

        return Task.CompletedTask;
    }
}