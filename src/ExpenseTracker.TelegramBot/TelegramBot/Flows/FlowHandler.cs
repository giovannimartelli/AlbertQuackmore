using ExpenseTracker.TelegramBot.TelegramBot.Utils;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ExpenseTracker.TelegramBot.TelegramBot.Flows;

/// <summary>
/// Abstract base class for handling Telegram bot flows.
/// Each flow (insert expense, download report, etc.) inherits from this class.
/// </summary>
public abstract class FlowHandler
{
    /// <summary>
    /// Returns the menu item information for this handler (display text and command).
    /// Returns null if this handler should not appear in the main menu.
    /// </summary>
    public abstract string? GetMenuItemInfo();

    /// <summary>
    /// Determines if this handler can handle a command from the main menu.
    /// </summary>
    /// <param name="command">The received command (e.g., "menu_insert", "💰 Inserisci spesa")</param>
    /// <returns>True if this handler handles the command</returns>
    public abstract bool CanHandleMenuCommand(string command);

    /// <summary>
    /// Handles the selection from the main menu (e.g., "Insert expense" button).
    /// This method starts the flow.
    /// </summary>
    public abstract Task HandleMenuSelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken);

    /// <summary>
    /// Determines if this handler can handle a callback query based on the current state.
    /// callbacks are always in form calbackname$_$callbackdata
    /// </summary>
    /// <param name="callbackName">The callbac name (e.g, "cat")</param>
    /// <param name="callbackData">The callback data (e.g., 123", "sub:456")</param>
    /// <param name="state">The current conversation state</param>
    /// <returns>True if this handler can handle this callback</returns>
    public abstract bool CanHandleCallback(string callbackName, string callbackData, ConversationState state);

    /// <summary>
    /// Handles inline callbacks (e.g., category selection, subcategory selection).
    /// </summary>
    public abstract Task HandleCallbackAsync(
        ITelegramBotClient botClient,
        string callbackName,
        string callbackData,
        CallbackQuery callbackQuery,
        ConversationState state,
        CancellationToken cancellationToken);

    /// <summary>
    /// Determines if this handler can handle free text input based on the current state.
    /// </summary>
    /// <param name="state">The current conversation state</param>
    /// <returns>True if this handler can handle the text input</returns>
    public abstract bool CanHandleTextInput(ConversationState state);

    /// <summary>
    /// Handles free text input (e.g., description, amount).
    /// </summary>
    public abstract Task HandleTextInputAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken);

    /// <summary>
    /// Determines if this handler can handle back on the current state.
    /// </summary>
    /// <param name="state"></param>
    /// <returns></returns>
    public abstract bool CanHandleBack(ConversationState state);

    /// <summary>
    /// Handles the "Back" button action for this handler.
    /// Returns true if the back action was handled, false if it should go to main menu.
    /// </summary>
    public abstract Task<bool> HandleBackAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken);

    /// <summary>
    /// Determines if this handler can handle WebApp data based on the current state.
    /// </summary>
    /// <param name="state">The current conversation state</param>
    /// <returns>True if this handler can handle WebApp data in this state</returns>
    public virtual bool CanHandleWebAppData(ConversationState state) => false;

    /// <summary>
    /// Handles data received from a Telegram WebApp (e.g., date picker).
    /// </summary>
    public virtual Task HandleWebAppDataAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Determines if this handler can handle a document based on the current state.
    /// </summary>
    /// <param name="state">The current conversation state</param>
    /// <returns>True if this handler can handle a document in this state</returns>
    public virtual bool CanHandleDocument(ConversationState state) => false;

    /// <summary>
    /// Handles a document (file) sent by the user.
    /// </summary>
    public virtual Task HandleDocumentAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken) => Task.CompletedTask;
}