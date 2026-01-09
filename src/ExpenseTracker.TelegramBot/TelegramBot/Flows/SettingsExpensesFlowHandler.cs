using System.Diagnostics;
using System.Globalization;
using ExpenseTracker.Services;
using ExpenseTracker.TelegramBot.TelegramBot.Utils;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExpenseTracker.TelegramBot.TelegramBot.Flows;

/// <summary>
/// Settings sub-flow for expense categories and subcategories.
/// Entry from SettingsRoot via callback settings_expenses/expenses.
/// Flow: select action -> (add category -> text) OR (add subcategory -> pick cat -> text)
/// </summary>
public class ExpensesFlowHandler(IServiceScopeFactory scopeFactory, ILogger<ExpensesFlowHandler> logger) : FlowHandler, ISubFlow
{
    // ISettingsSubFlow contract
    public string SettingsMenuText => "⚙️ Impostazioni spese";
    public string SettingsCallbackName => CallbackSettingsExpenses;
    public string SettingsCallbackData => "expenses";
    public string SettingsEntryStep => StepSelectAction;

    private const string CallbackSettingsExpenses = "settings_expenses";
    private const string CallbackAddCategory = "settings_addcat";
    private const string CallbackAddSubCategory = "settings_addsub";
    private const string CallbackPickCategory = "settings_pickcat";

    private const string StepSelectAction = "SettingsExpenses_SelectAction";
    private const string StepAddCategory = "SettingsExpenses_AddCategory";
    private const string StepSelectCategoryForSub = "SettingsExpenses_SelectCategoryForSub";
    private const string StepAddSubCategory = "SettingsExpenses_AddSubCategory";

    public override string? GetMenuItemInfo() => null;

    public override bool CanHandleMenuCommand(string command) => false;

    public override Task HandleMenuSelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        // Not invoked directly from main menu
        throw new UnreachableException($"Can't be there if {CanHandleTextInput} returns false");
    }

    public override bool CanHandleCallback(string callbackName, string callbackData, ConversationState state)
    {
        return callbackName switch
        {
            CallbackSettingsExpenses => callbackData == SettingsCallbackData,
            CallbackAddCategory or CallbackAddSubCategory => state.Step == StepSelectAction,
            CallbackPickCategory => state.Step == StepSelectCategoryForSub,
            _ => false
        };
    }

    public async Task StartFromSettingsRootAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        state.Step = StepSelectAction;
        await ShowActionsAsync(botClient, chat, state, cancellationToken);
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

        switch (callbackName)
        {
            case CallbackSettingsExpenses when callbackData == SettingsCallbackData:
                state.Step = StepSelectAction;
                await ShowActionsAsync(botClient, chat, state, cancellationToken);
                return;

            case CallbackAddCategory:
                state.Step = StepAddCategory;
                await AskForCategoryNameAsync(botClient, chat, state, cancellationToken);
                return;

            case CallbackAddSubCategory:
                state.Step = StepSelectCategoryForSub;
                await ShowCategoriesForSubAsync(botClient, chat, state, cancellationToken);
                return;

            case CallbackPickCategory:
                var categoryId = int.Parse(callbackData, CultureInfo.InvariantCulture);
                using (var scope = scopeFactory.CreateScope())
                {
                    var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
                    var category = await categoryService.GetCategoryByIdAsync(categoryId);
                    if (category is null)
                    {
                        await botClient.SendMessage(chat.Id, "❌ Categoria non trovata.", cancellationToken: cancellationToken);
                        return;
                    }

                    state.SelectedCategoryId = category.Id;
                    state.SelectedCategoryName = category.Name;
                }

                state.Step = StepAddSubCategory;
                await AskForSubCategoryNameAsync(botClient, chat, state, cancellationToken);
                return;
        }
    }

    public override bool CanHandleTextInput(ConversationState state) =>
        state.Step is StepAddCategory or StepAddSubCategory;

    public override async Task HandleTextInputAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;
        var text = message.Text!.Trim();

        if (state.Step == StepAddCategory)
        {
            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            await categoryService.CreateNewCategoryAsync(text);

            logger.LogInformation("Created category {Name}", text);
            state.Reset();
            // Re-enter settings expenses menu
            state.Step = StepSelectAction;
            await ShowActionsAsync(botClient, chat, state, cancellationToken, $"✅ Categoria *{text}* creata");
        }
        else if (state.Step == StepAddSubCategory)
        {
            if (state.SelectedCategoryId is null)
            {
                await botClient.SendMessage(chat.Id, "❌ Seleziona prima una categoria.", cancellationToken: cancellationToken);
                state.Step = StepSelectCategoryForSub;
                await ShowCategoriesForSubAsync(botClient, chat, state, cancellationToken);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            await categoryService.CreateNewSubCategoryAsync(text, state.SelectedCategoryId.Value);

            logger.LogInformation("Created subcategory {Name} under category {CategoryId}", text, state.SelectedCategoryId);
            var createdFor = state.SelectedCategoryName;
            state.Reset();
            state.Step = StepSelectAction;
            await ShowActionsAsync(botClient, chat, state, cancellationToken, $"✅ Sottocategoria *{text}* creata in *{createdFor}*");
        }
    }

    public override bool CanHandleBack(ConversationState state) =>
        state.Step is StepSelectAction or StepAddCategory or StepSelectCategoryForSub or StepAddSubCategory;

    public override async Task<bool> HandleBackAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Settings expenses back from step {Step}", state.Step);

        switch (state.Step)
        {
            case StepSelectAction:
                // Go back to settings root via controller
                state.Step = SettingsFlowHandler.SettingsRootStep;
                return false;
            case StepAddCategory:
            case StepSelectCategoryForSub:
                state.Step = StepSelectAction;
                await ShowActionsAsync(botClient, chat, state, cancellationToken);
                return true;
            case StepAddSubCategory:
                state.Step = StepSelectCategoryForSub;
                await ShowCategoriesForSubAsync(botClient, chat, state, cancellationToken);
                return true;
            default:
                return false;
        }
    }

    private async Task ShowActionsAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken,
        string? headerOverride = null)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.ButtonWithCallbackdata("➕ Categoria", CallbackAddCategory, "start")],
            [Utils.Utils.ButtonWithCallbackdata("➕ Sottocategoria", CallbackAddSubCategory, "start")],
            [Utils.Utils.Back],
            [Utils.Utils.MainMenu]
        ]);

        var msg = await botClient.TryEditMessageText(
            chat.Id,
            state.LastBotMessageId,
            headerOverride ?? "⚙️ *Impostazioni spese*",
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task AskForCategoryNameAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.Back],
            [Utils.Utils.MainMenu]
        ]);

        var msg = await botClient.TryEditMessageText(
            chat.Id,
            state.LastBotMessageId,
            "✏️ Inserisci il nome della nuova categoria:",
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task ShowCategoriesForSubAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
        var categories = await categoryService.GetAllCategoriesAsync();

        var buttons = categories
            .Select(c => new[] { Utils.Utils.ButtonWithCallbackdata(c.Name, CallbackPickCategory, c.Id) })
            .ToList();

        buttons.Add([Utils.Utils.Back]);
        buttons.Add([Utils.Utils.MainMenu]);

        var keyboard = new InlineKeyboardMarkup(buttons);

        var msg = await botClient.TryEditMessageText(
            chat.Id,
            state.LastBotMessageId,
            "📁 Seleziona la categoria per la nuova sottocategoria:",
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task AskForSubCategoryNameAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.Back],
            [Utils.Utils.MainMenu]
        ]);

        var msg = await botClient.TryEditMessageText(
            chat.Id,
            state.LastBotMessageId,
            $"✏️ Inserisci il nome della nuova sottocategoria per *{state.SelectedCategoryName}*:",
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }
}
