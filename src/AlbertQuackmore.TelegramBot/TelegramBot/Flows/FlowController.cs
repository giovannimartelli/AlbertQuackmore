using AlbertQuackmore.TelegramBot.TelegramBot.Utils;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AlbertQuackmore.TelegramBot.TelegramBot.Flows;

/// <summary>
/// Controller/Broker that routes requests to various FlowHandlers.
/// Implements the Mediator pattern to orchestrate bot flows.
/// </summary>
public class FlowController
{
    private readonly List<FlowHandler> _handlers;
    private readonly ILogger<FlowController> _logger;

    public ReplyKeyboardMarkup MainMenuKeyboard { get; }

    public FlowController(IEnumerable<FlowHandler> handlers, ILogger<FlowController> logger)
    {
        _handlers = handlers.ToList();
        _logger = logger;
        MainMenuKeyboard = GenerateMainMenuKeyboard();
    }

    private ReplyKeyboardMarkup GenerateMainMenuKeyboard()
    {
        var menuItems = _handlers
            .Select(h => h.GetMenuItemInfo())
            .Where(info => info != null)
            .Select(info => new KeyboardButton(info!))
            .ToArray();

        // Arrange buttons in rows of 2
        var rows = menuItems
            .Chunk(2)
            .Select(chunk => chunk.ToArray())
            .ToArray();

        return new ReplyKeyboardMarkup(rows)
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = false
        };
    }

    /// <summary>
    /// Deletes all tracked flow messages and clears the list.
    /// This should be called when returning to the main menu.
    /// </summary>
    public async Task DeleteFlowMessagesAsync(
        ITelegramBotClient botClient,
        long chatId,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        if (state.FlowMessageIds.Count == 0)
            return;

        _logger.LogInformation("Deleting {Count} flow messages for chat {ChatId}", state.FlowMessageIds.Count, chatId);

        foreach (var messageId in state.FlowMessageIds)
        {
            try
            {
                await botClient.DeleteMessage(chatId, messageId, cancellationToken);
                _logger.LogDebug("Deleted flow message {MessageId}", messageId);
            }
            catch (ApiRequestException ex)
            {
                _logger.LogDebug("Could not delete flow message {MessageId}: {Error}", messageId, ex.Message);
            }
        }

        state.ClearFlowMessages();
        state.ResetToMainMenu();
    }

    /// <summary>
    /// Handles a text message from the user.
    /// </summary>
    public async Task HandleTextMessageAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var text = message.Text!;
        var chat = message.Chat;

        // Special commands
        if (text.StartsWith("/start"))
        {
            await ShowMainMenuAsync(botClient, chat, state, true, cancellationToken);
            return;
        }

        // Check if it's a command from the main menu
        var menuHandler = _handlers.SingleOrDefault(h => h.CanHandleMenuCommand(text));
        if (menuHandler != null)
        {
            _logger.LogInformation("Menu command '{Command}' handled by {HandlerType}", text, menuHandler.GetType().Name);
            // Delete any existing flow messages before starting a new flow
            await DeleteFlowMessagesAsync(botClient, chat.Id, state, cancellationToken);
            state.Reset();
            await menuHandler.HandleMenuSelectionAsync(botClient, chat, state, cancellationToken);
            return;
        }

        // Check if it's text input for an active flow
        var textHandler = _handlers.SingleOrDefault(h => h.CanHandleTextInput(state));
        if (textHandler != null)
        {
            _logger.LogInformation("Text input handled by {HandlerType} in step {Step}", textHandler.GetType().Name, state.CurrentStep);
            await textHandler.HandleTextInputAsync(botClient, message, state, cancellationToken);
            // await DeleteLastBotMessage(botClient, chat, state, cancellationToken);
            return;
        }

        // No handler found
        _logger.LogWarning("No handler found for text '{Text}' in step {Step}", text, state.CurrentStep);
        await botClient.SendFlowMessageAsync(
            chatId: chat.Id,
            state,
            text: "❓ I didn't understand. Use /start to get started.",
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Handles a callback query (inline button click).
    /// </summary>
    public async Task HandleCallbackQueryAsync(
        ITelegramBotClient botClient,
        CallbackQuery callbackQuery,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var callbackData = callbackQuery.Data!;
        var chat = callbackQuery.Message!.Chat;

        // Special callbacks handled by the controller
        if (callbackData == Utils.Utils.CallbackMainMenu)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            await ShowMainMenuAsync(botClient, chat, state, false, cancellationToken);
            return;
        }

        if (callbackData == Utils.Utils.CallbackBack)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            await HandleBackAsync(botClient, chat, state, cancellationToken);
            return;
        }

        var callbackName = callbackData.Split(Utils.Utils.CallbackSeparator)[0];
        callbackData = callbackData.Split(Utils.Utils.CallbackSeparator)[1];
        // Find a handler that can handle this callback
        var handler = _handlers.FirstOrDefault(h => h.CanHandleCallback(callbackName, callbackData, state));
        if (handler != null)
        {
            _logger.LogInformation("Callback '{CallbackData}' handled by {HandlerType}", callbackData, handler.GetType().Name);
            await handler.HandleCallbackAsync(botClient, callbackName, callbackData, callbackQuery, state, cancellationToken);
            return;
        }

        // No handler found
        _logger.LogWarning("No handler found for callback '{CallbackData}' in step {Step}", callbackData, state.CurrentStep);
        await botClient.AnswerCallbackQuery(
            callbackQuery.Id,
            text: "❓ Unrecognized action",
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Shows the main menu to the user.
    /// </summary>
    private async Task ShowMainMenuAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        bool isCommand,
        CancellationToken cancellationToken)
    {
        // Delete all flow messages before returning to main menu
        await DeleteFlowMessagesAsync(botClient, chat.Id, state, cancellationToken);
        const string text = "👋 *Main Menu*\n\nChoose an operation:";
        if (isCommand || state.MainMenuMessageId == null)
        {
            // Send a new main menu message (on /start command or if no main menu exists)
            var msg = await botClient.SendMessage(
                chatId: chat.Id,
                text: text,
                parseMode: ParseMode.Markdown,
                replyMarkup: MainMenuKeyboard,
                cancellationToken: cancellationToken);

            // Store the main menu message ID
            state.MainMenuMessageId = msg.MessageId;
            state.LastBotMessageId = msg.MessageId;
        }        
        state.Reset();

    }

    /// <summary>
    /// Handles the "Back" button by delegating to the active handler.
    /// If no handler can handle the back action, returns to the main menu.
    /// </summary>
    private async Task HandleBackAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling back from step {Step}", state.CurrentStep);

        // Find the active handler (one that can handle the current state)
        var activeHandler = _handlers.FirstOrDefault(h => h.CanHandleBack(state));

        if (activeHandler != null)
        {
            var handled = await activeHandler.HandleBackAsync(botClient, chat, state, cancellationToken);
            if (handled)
            {
                return;
            }
        }

        // No handler could handle the back action, return to main menu
        await ShowMainMenuAsync(botClient, chat, state, false, cancellationToken);
    }

    /// <summary>
    /// Handles data received from a Telegram WebApp.
    /// </summary>
    public async Task HandleWebAppDataAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;

        // Find a handler that can handle WebApp data in the current state
        var handler = _handlers.FirstOrDefault(h => h.CanHandleWebAppData(state));
        if (handler != null)
        {
            _logger.LogInformation("WebApp data handled by {HandlerType} in step {Step}", handler.GetType().Name, state.CurrentStep);
            await handler.HandleWebAppDataAsync(botClient, message, state, cancellationToken);
            return;
        }

        // No handler found
        _logger.LogWarning("No handler found for WebApp data in step {Step}", state.CurrentStep);
        await botClient.SendFlowMessageAsync(
            chatId: chat.Id,
            state,
            text: "❓ Unexpected data received.",
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Handles a document (file) sent by the user.
    /// </summary>
    public async Task HandleDocumentAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;

        // Find a handler that can handle document in the current state
        var handler = _handlers.FirstOrDefault(h => h.CanHandleDocument(state));
        if (handler != null)
        {
            _logger.LogInformation("Document handled by {HandlerType} in step {Step}", handler.GetType().Name, state.CurrentStep);
            await handler.HandleDocumentAsync(botClient, message, state, cancellationToken);
            return;
        }

        // No handler found
        _logger.LogWarning("No handler found for document in step {Step}", state.CurrentStep);
        await botClient.SendFlowMessageAsync(
            chatId: chat.Id,
            state,
            text: "❓ Non ero in attesa di un file. Usa il menu per iniziare l'importazione.",
            cancellationToken: cancellationToken);
    }
}