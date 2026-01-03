using ExpenseTracker.Services;
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
public class SettingsFlowHandler(IServiceScopeFactory scopeFactory, ILogger<SettingsFlowHandler> logger) : FlowHandler
{
    private const string MenuCommandText = "⚙️ Settings";
    
    private const string SettingsRootStep = "Settings_Root";
    private const string SettingsExpensesStep = "Settings_Expenses";
    private const string AddCategoryStep = "Settings_AddCategory";
    private const string SelectCategoryForSubStep = "Settings_SelectCategoryForSub";
    private const string AddSubCategoryStep = "Settings_AddSubCategory";

    private const string CallbackSettingsRoot = "settings_root";
    private const string CallbackSettingsExpenses = "settings_expenses";
    private const string CallbackAddCategory = "settings_addcat";
    private const string CallbackAddSubCategory = "settings_addsub";
    private const string CallbackPickCategory = "settings_pickcat";

    public override string? GetMenuItemInfo() => MenuCommandText;
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
        return callbackName switch
        {
            CallbackSettingsRoot => true,
            CallbackSettingsExpenses => true,
            CallbackAddCategory => state.Step is SettingsExpensesStep or AddCategoryStep,
            CallbackAddSubCategory => state.Step is SettingsExpensesStep or SelectCategoryForSubStep or AddSubCategoryStep,
            CallbackPickCategory => state.Step is SelectCategoryForSubStep,
            _ => false
        };
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
            case CallbackSettingsRoot:
                state.Step = SettingsRootStep;
                await ShowSettingsRootAsync(botClient, chat, state, cancellationToken);
                return;

            case CallbackSettingsExpenses:
                if (callbackData == "expenses")
                {
                    state.Step = SettingsExpensesStep;
                    await ShowExpensesSettingsAsync(botClient, chat, state, "⚙️ *Impostazioni spese*", cancellationToken);
                }
                return;

            case CallbackAddCategory:
                state.Step = AddCategoryStep;
                await AskForCategoryNameAsync(botClient, chat, state, cancellationToken);
                return;

            case CallbackAddSubCategory:
                state.Step = SelectCategoryForSubStep;
                await ShowCategoriesForSubAsync(botClient, chat, state, cancellationToken);
                return;

            case CallbackPickCategory:
                var categoryId = int.Parse(callbackData);
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

                state.Step = AddSubCategoryStep;
                await AskForSubCategoryNameAsync(botClient, chat, state, cancellationToken);
                return;
        }
    }

    public override bool CanHandleTextInput(ConversationState state) =>
        state.Step is AddCategoryStep or AddSubCategoryStep;

    public override async Task HandleTextInputAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;
        var text = message.Text!.Trim();

        if (state.Step == AddCategoryStep)
        {
            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            await categoryService.CreateNewCategoryAsync(text);

            logger.LogInformation("Created category {Name}", text);
            state.SelectedCategoryId = null;
            state.SelectedCategoryName = null;
            state.Step = SettingsExpensesStep;
            await ShowExpensesSettingsAsync(botClient, chat, state, $"✅ Categoria *{text}* creata", cancellationToken);
        }
        else if (state.Step == AddSubCategoryStep)
        {
            if (state.SelectedCategoryId is null)
            {
                await botClient.SendMessage(chat.Id, "❌ Seleziona prima una categoria.", cancellationToken: cancellationToken);
                state.Step = SelectCategoryForSubStep;
                await ShowCategoriesForSubAsync(botClient, chat, state, cancellationToken);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            await categoryService.CreateNewSubCategoryAsync(text, state.SelectedCategoryId.Value);

            logger.LogInformation("Created subcategory {Name} under category {CategoryId}", text, state.SelectedCategoryId);
            var createdFor = state.SelectedCategoryName;
            state.SelectedCategoryId = null;
            state.SelectedCategoryName = null;
            state.Step = SettingsExpensesStep;
            await ShowExpensesSettingsAsync(botClient, chat, state, $"✅ Sottocategoria *{text}* creata in *{createdFor}*", cancellationToken);
        }
    }

    public override bool CanHandleBack(ConversationState state) =>
        state.Step is SettingsExpensesStep or AddCategoryStep or SelectCategoryForSubStep or AddSubCategoryStep;

    public override async Task<bool> HandleBackAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Settings back from step {Step}", state.Step);

        switch (state.Step)
        {
            case SettingsExpensesStep:
                state.Step = SettingsRootStep;
                await ShowSettingsRootAsync(botClient, chat, state, cancellationToken);
                return true;
            case AddCategoryStep:
                state.Step = SettingsExpensesStep;
                await ShowExpensesSettingsAsync(botClient, chat, state, "⚙️ *Impostazioni spese*", cancellationToken);
                return true;
            case SelectCategoryForSubStep:
                state.Step = SettingsExpensesStep;
                await ShowExpensesSettingsAsync(botClient, chat, state, "⚙️ *Impostazioni spese*", cancellationToken);
                return true;
            case AddSubCategoryStep:
                state.Step = SelectCategoryForSubStep;
                await ShowCategoriesForSubAsync(botClient, chat, state, cancellationToken);
                return true;
            default:
                return false;
        }
    }

    private async Task ShowSettingsRootAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.ButtonWithCallbackdata("⚙️ Impostazioni spese", CallbackSettingsExpenses, "expenses")],
            [Utils.Utils.MainMenu]
        ]);

        var msg = await botClient.TryEditMessageText(
            chat.Id,
            state.LastBotMessageId,
            "⚙️ *Impostazioni*\nSeleziona una sezione:",
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task ShowExpensesSettingsAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        string header,
        CancellationToken cancellationToken)
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
            header,
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
