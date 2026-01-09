using ExpenseTracker.TelegramBot.TelegramBot.Utils;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExpenseTracker.TelegramBot.TelegramBot.Flows;

/// <summary>
/// Settings flow that acts as a menu and hosts settings subflows.
/// Currently supports expense settings: add category and add subcategory.
/// </summary>
public class SettingsFlowHandler(IServiceProvider serviceProvider, ILogger<SettingsFlowHandler> logger) : FlowHandler
{
    public const string SettingsRootStep = "Settings_Root";
    private const string MenuCommandText = "⚙️ Settings";
    
    private List<ISubFlow> SubFlows => serviceProvider.GetServices<FlowHandler>().OfType<ISubFlow>().ToList();

    public override string GetMenuItemInfo() => MenuCommandText;
    public override bool CanHandleMenuCommand(string command) => command == MenuCommandText;

    public override async Task HandleMenuSelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Opening settings root for chat {ChatId}", chat.Id);
        state.Step = SettingsRootStep;
        await ShowSettingsRootAsync(botClient, chat, state, cancellationToken);
    }

    public override bool CanHandleCallback(string callbackName, string callbackData, ConversationState state)
    {
        // Delegate to subflows only when we are in settings root
        return state.Step == SettingsRootStep && SubFlows.Any(f => f.SettingsCallbackName == callbackName && f.SettingsCallbackData == callbackData);
    }

    public override async Task HandleCallbackAsync(
        ITelegramBotClient botClient,
        string callbackName,
        string callbackData,
        CallbackQuery callbackQuery,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = callbackQuery.Message!.Chat;
        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
        
        var targetSubFlow = SubFlows.FirstOrDefault(f => f.SettingsCallbackName == callbackName && f.SettingsCallbackData == callbackData);
        if (targetSubFlow != null)
        {
            state.Step = targetSubFlow.SettingsEntryStep;
            await targetSubFlow.StartFromSettingsRootAsync(botClient, chat, state, cancellationToken);
        }
    }

    public override bool CanHandleTextInput(ConversationState state) => false;

    public override Task HandleTextInputAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public override bool CanHandleBack(ConversationState state) => state.Step == SettingsRootStep;

    public override async Task<bool> HandleBackAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        state.Step = SettingsRootStep;
        await ShowSettingsRootAsync(botClient, chat, state, cancellationToken);
        return true;
    }

    private async Task ShowSettingsRootAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var buttons = SubFlows
            .Select(flow => new[]
            {
                Utils.Utils.ButtonWithCallbackdata(flow.SettingsMenuText, flow.SettingsCallbackName, flow.SettingsCallbackData)
            })
            .ToList();

        buttons.Add([Utils.Utils.MainMenu]);
        var keyboard =  new InlineKeyboardMarkup(buttons);

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            "⚙️ *Impostazioni*\nSeleziona una sezione:",
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }
}
