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
[Flow("SettingsExpenses")]
public class ExpensesFlowHandler(
    IServiceScopeFactory scopeFactory,
    IServiceProvider serviceProvider,
    ILogger<ExpensesFlowHandler> logger) : FlowHandler, ISubFlow
{
    // ISubFlow contract
    public string SettingsMenuText => "⚙️ Impostazioni spese";
    public string SettingsCallbackName => CallbackSettingsExpenses;
    public string SettingsCallbackData => "expenses";

    private const string CallbackSettingsExpenses = "settings_expenses";
    private const string CallbackAddCategory = "settings_addcat";
    private const string CallbackAddSubCategory = "settings_addsub";
    private const string CallbackPickCategory = "settings_pickcat";
    private const string CallbackAddTag = "settings_addtag";
    private const string CallbackSkipTags = "settings_skiptags";
    private const string CallbackDoneTags = "settings_donetags";

    public override string? GetMenuItemInfo() => null;

    public override bool CanHandleMenuCommand(string command) => false;

    public override Task HandleMenuSelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        throw new UnreachableException("ExpensesFlowHandler is a sub-flow, not a main menu item");
    }

    public override bool CanHandleCallback(string callbackName, string callbackData, ConversationState state)
    {
        var flowData = state.GetFlowData<SettingsExpensesFlowData>();
        if (flowData == null) return false;

        return callbackName switch
        {
            CallbackSettingsExpenses => callbackData == SettingsCallbackData,
            CallbackAddCategory or CallbackAddSubCategory => flowData.CurrentStep == SettingsExpensesFlowData.StepSelectAction,
            CallbackPickCategory => flowData.CurrentStep == SettingsExpensesFlowData.StepSelectCategoryForSub,
            CallbackAddTag or CallbackSkipTags => flowData.CurrentStep == SettingsExpensesFlowData.StepAskAddTag,
            CallbackDoneTags => flowData.CurrentStep == SettingsExpensesFlowData.StepAddTag,
            _ => false
        };
    }

    public async Task StartFromSettingsRootAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var flowData = new SettingsExpensesFlowData();
        state.SetFlowData(flowData);

        await ShowActionsAsync(botClient, chat, state, flowData, cancellationToken);
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
        var flowData = state.GetFlowData<SettingsExpensesFlowData>()!;

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

        switch (callbackName)
        {
            case CallbackSettingsExpenses when callbackData == SettingsCallbackData:
                flowData.CurrentStep = SettingsExpensesFlowData.StepSelectAction;
                await ShowActionsAsync(botClient, chat, state, flowData, cancellationToken);
                return;

            case CallbackAddCategory:
                flowData.CurrentStep = SettingsExpensesFlowData.StepAddCategory;
                await AskForCategoryNameAsync(botClient, chat, state, cancellationToken);
                return;

            case CallbackAddSubCategory:
                flowData.CurrentStep = SettingsExpensesFlowData.StepSelectCategoryForSub;
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

                    flowData.CategoryId = category.Id;
                    flowData.CategoryName = category.Name;
                }

                flowData.CurrentStep = SettingsExpensesFlowData.StepAddSubCategory;
                await AskForSubCategoryNameAsync(botClient, chat, state, flowData, cancellationToken);
                return;

            case CallbackAddTag:
                flowData.CurrentStep = SettingsExpensesFlowData.StepAddTag;
                await AskForTagNameAsync(botClient, chat, state, flowData, cancellationToken);
                return;

            case CallbackSkipTags:
            case CallbackDoneTags:
                var createdSubCatName = flowData.CreatedSubCategoryName;
                var createdForCat = flowData.CategoryName;

                // Return to settings root
                var settingsFlowData = new SettingsFlowData();
                state.SetFlowData(settingsFlowData);

                var settingsHandler = serviceProvider.GetServices<FlowHandler>()
                    .OfType<SettingsFlowHandler>()
                    .First();

                await settingsHandler.ShowSettingsRootAsync(botClient, chat, state, cancellationToken);
                return;
        }
    }

    public override bool CanHandleTextInput(ConversationState state)
    {
        var flowData = state.GetFlowData<SettingsExpensesFlowData>();
        if (flowData == null) return false;

        return flowData.CurrentStep is SettingsExpensesFlowData.StepAddCategory
            or SettingsExpensesFlowData.StepAddSubCategory
            or SettingsExpensesFlowData.StepAddTag;
    }

    public override async Task HandleTextInputAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;
        var text = message.Text!.Trim();
        var flowData = state.GetFlowData<SettingsExpensesFlowData>()!;

        if (flowData.CurrentStep == SettingsExpensesFlowData.StepAddCategory)
        {
            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            await categoryService.CreateNewCategoryAsync(text);

            logger.LogInformation("Created category {Name}", text);

            flowData.CurrentStep = SettingsExpensesFlowData.StepSelectAction;
            await ShowActionsAsync(botClient, chat, state, flowData, cancellationToken, $"✅ Categoria *{text}* creata");
        }
        else if (flowData.CurrentStep == SettingsExpensesFlowData.StepAddSubCategory)
        {
            if (flowData.CategoryId is null)
            {
                await botClient.SendMessage(chat.Id, "❌ Seleziona prima una categoria.", cancellationToken: cancellationToken);
                flowData.CurrentStep = SettingsExpensesFlowData.StepSelectCategoryForSub;
                await ShowCategoriesForSubAsync(botClient, chat, state, cancellationToken);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            var createdSubCategory = await categoryService.CreateNewSubCategoryAsync(text, flowData.CategoryId.Value);

            logger.LogInformation("Created subcategory {Name} under category {CategoryId}", text, flowData.CategoryId);

            flowData.CreatedSubCategoryId = createdSubCategory.Id;
            flowData.CreatedSubCategoryName = createdSubCategory.Name;
            flowData.CurrentStep = SettingsExpensesFlowData.StepAskAddTag;
            await AskIfAddTagsAsync(botClient, chat, state, flowData, cancellationToken);
        }
        else if (flowData.CurrentStep == SettingsExpensesFlowData.StepAddTag)
        {
            if (flowData.CreatedSubCategoryId is null)
            {
                await botClient.SendMessage(chat.Id, "❌ Errore: sottocategoria non trovata.", cancellationToken: cancellationToken);
                flowData.CurrentStep = SettingsExpensesFlowData.StepSelectAction;
                await ShowActionsAsync(botClient, chat, state, flowData, cancellationToken);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            await categoryService.CreateTagAsync(text, flowData.CreatedSubCategoryId.Value);

            logger.LogInformation("Created tag {Name} for subcategory {SubCategoryId}", text, flowData.CreatedSubCategoryId);

            await ShowTagAddedAsync(botClient, chat, state, text, cancellationToken);
        }
    }

    public override bool CanHandleBack(ConversationState state)
    {
        var flowData = state.GetFlowData<SettingsExpensesFlowData>();
        if (flowData == null) return false;

        return flowData.CurrentStep is SettingsExpensesFlowData.StepSelectAction
            or SettingsExpensesFlowData.StepAddCategory
            or SettingsExpensesFlowData.StepSelectCategoryForSub
            or SettingsExpensesFlowData.StepAddSubCategory
            or SettingsExpensesFlowData.StepAskAddTag
            or SettingsExpensesFlowData.StepAddTag;
    }

    public override async Task<bool> HandleBackAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var flowData = state.GetFlowData<SettingsExpensesFlowData>()!;
        logger.LogInformation("Settings expenses back from step {Step}", flowData.CurrentStep);

        switch (flowData.CurrentStep)
        {
            case SettingsExpensesFlowData.StepSelectAction:
                // Go back to settings root
                var settingsFlowData = new SettingsFlowData();
                state.SetFlowData(settingsFlowData);

                var settingsHandler = serviceProvider.GetServices<FlowHandler>()
                    .OfType<SettingsFlowHandler>()
                    .First();
                await settingsHandler.ShowSettingsRootAsync(botClient, chat, state, cancellationToken);
                return true;

            case SettingsExpensesFlowData.StepAddCategory:
            case SettingsExpensesFlowData.StepSelectCategoryForSub:
                flowData.CurrentStep = SettingsExpensesFlowData.StepSelectAction;
                await ShowActionsAsync(botClient, chat, state, flowData, cancellationToken);
                return true;

            case SettingsExpensesFlowData.StepAddSubCategory:
                flowData.CurrentStep = SettingsExpensesFlowData.StepSelectCategoryForSub;
                await ShowCategoriesForSubAsync(botClient, chat, state, cancellationToken);
                return true;

            case SettingsExpensesFlowData.StepAskAddTag:
            case SettingsExpensesFlowData.StepAddTag:
                // When going back from tag flow, finish with subcategory creation confirmation
                var createdSubCatName = flowData.CreatedSubCategoryName;
                var createdForCat = flowData.CategoryName;

                // Reset for next action
                flowData.CreatedSubCategoryId = null;
                flowData.CreatedSubCategoryName = null;
                flowData.CurrentStep = SettingsExpensesFlowData.StepSelectAction;

                await ShowActionsAsync(botClient, chat, state, flowData, cancellationToken,
                    $"✅ Sottocategoria *{createdSubCatName}* creata in *{createdForCat}*");
                return true;

            default:
                return false;
        }
    }

    private async Task ShowActionsAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        SettingsExpensesFlowData flowData,
        CancellationToken cancellationToken,
        string? headerOverride = null)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.ButtonWithCallbackdata("+ Inserisci Categoria", CallbackAddCategory, "start"),
             Utils.Utils.ButtonWithCallbackdata("+ Inserisci Sottocategoria", CallbackAddSubCategory, "start")],
            [Utils.Utils.Back],
            [Utils.Utils.MainMenu]
        ]);

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
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

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
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

        // Arrange category buttons in rows of 2
        var categoryButtons = categories
            .Select(c => Utils.Utils.ButtonWithCallbackdata(c.Name, CallbackPickCategory, c.Id))
            .ToArray();

        var buttons = categoryButtons
            .Chunk(2)
            .Select(chunk => chunk.ToArray())
            .ToList();

        buttons.Add([Utils.Utils.Back]);
        buttons.Add([Utils.Utils.MainMenu]);

        var keyboard = new InlineKeyboardMarkup(buttons);

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
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
        SettingsExpensesFlowData flowData,
        CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.Back],
            [Utils.Utils.MainMenu]
        ]);

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            $"✏️ Inserisci il nome della nuova sottocategoria per *{flowData.CategoryName}*:",
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task AskIfAddTagsAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        SettingsExpensesFlowData flowData,
        CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.ButtonWithCallbackdata("➕ Aggiungi tag", CallbackAddTag, "start"),
             Utils.Utils.ButtonWithCallbackdata("⏭️ Salta", CallbackSkipTags, "skip")],
            [Utils.Utils.MainMenu]
        ]);

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            $"✅ Sottocategoria *{flowData.CreatedSubCategoryName}* creata\\!\n\nVuoi aggiungere dei tag?",
            ParseMode.MarkdownV2,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task AskForTagNameAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        SettingsExpensesFlowData flowData,
        CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.Back],
            [Utils.Utils.MainMenu]
        ]);

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            $"🏷️ Inserisci il nome del tag per *{flowData.CreatedSubCategoryName}*:",
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task ShowTagAddedAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        string tagName,
        CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.ButtonWithCallbackdata("✅ Fatto", CallbackDoneTags, "done")],
            [Utils.Utils.MainMenu]
        ]);

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            $"✅ Tag *{tagName}* aggiunto\\!\n\nVuoi aggiungere un altro tag?",
            ParseMode.MarkdownV2,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }
}
