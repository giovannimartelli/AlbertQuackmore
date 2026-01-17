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
[Flow("Settings")]
public class SettingsFlowHandler(IServiceProvider serviceProvider, ILogger<SettingsFlowHandler> logger) : FlowHandler
{
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

        var flowData = new SettingsFlowData();
        state.SetFlowData(flowData);

        await ShowSettingsRootAsync(botClient, chat, state, cancellationToken);
    }

    public override bool CanHandleCallback(string callbackName, string callbackData, ConversationState state)
    {
        var flowData = state.GetFlowData<SettingsFlowData>();
        if (flowData == null) return false;

        // Delegate to subflows only when we are in settings root
        return flowData.CurrentStep == SettingsFlowData.StepRoot
               && SubFlows.Any(f => f.SettingsCallbackName == callbackName && f.SettingsCallbackData == callbackData);
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
            await targetSubFlow.StartFromSettingsRootAsync(botClient, chat, state, cancellationToken);
        }
    }

    public override bool CanHandleTextInput(ConversationState state) => false;

    public override Task HandleTextInputAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public override bool CanHandleBack(ConversationState state)
    {
        var flowData = state.GetFlowData<SettingsFlowData>();
        return flowData?.CurrentStep == SettingsFlowData.StepRoot;
    }

    public override async Task<bool> HandleBackAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var flowData = state.GetFlowData<SettingsFlowData>();
        if (flowData != null)
        {
            flowData.CurrentStep = SettingsFlowData.StepRoot;
        }
        await ShowSettingsRootAsync(botClient, chat, state, cancellationToken);
        return true;
    }

    public async Task ShowSettingsRootAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        // Arrange subflow buttons in rows of 2
        var subFlowButtons = SubFlows
            .Select(flow => Utils.Utils.ButtonWithCallbackdata(flow.SettingsMenuText, flow.SettingsCallbackName, flow.SettingsCallbackData))
            .ToArray();

        var buttons = subFlowButtons
            .Chunk(2)
            .Select(chunk => chunk.ToArray())
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
