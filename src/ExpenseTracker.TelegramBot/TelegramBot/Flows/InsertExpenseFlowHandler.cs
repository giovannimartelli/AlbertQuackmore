using System.Globalization;
using ExpenseTracker.Services;
using ExpenseTracker.TelegramBot.TelegramBot.Utils;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExpenseTracker.TelegramBot.TelegramBot.Flows;

/// <summary>
/// Handles the flow for inserting a new expense.
/// Flow: Menu → Category → Subcategory → Description → Amount → Save
/// </summary>
public class InsertExpenseFlowHandler(IServiceScopeFactory scopeFactory, ILogger<InsertExpenseFlowHandler> logger) : FlowHandler
{
    private const string MenuCommandText = "💰 Inserisci spesa";

    private const string CallbackCategoryPrefix = "addexpenses_cat";
    private const string CallbackSubCategoryPrefix = "addexpenses_sub";

    private const string SelectCategoryStep = "AddExpense_SelectCategory";
    private const string SelectSubCategoryStep = "AddExpense_SelectSubCategory";
    private const string AddDescriptionStep = "AddExpense_AddDescription";
    private const string InsertAmountStep = "AddExpense_InsertAmount";

    public override string GetMenuItemInfo() => MenuCommandText;
    public override bool CanHandleMenuCommand(string command) => command == MenuCommandText;

    public override async Task HandleMenuSelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting insert expense flow for chat {ChatId}", chat.Id);
        state.Step = SelectCategoryStep;
        await ShowCategoriesAsync(botClient, chat, state, cancellationToken);
    }

    public override bool CanHandleCallback(string callbackName, string callbackData, ConversationState state)
    {
        return state.Step switch
        {
            SelectCategoryStep => callbackName == CallbackCategoryPrefix,
            SelectSubCategoryStep => callbackName == CallbackSubCategoryPrefix,
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

        if (callbackName == CallbackCategoryPrefix)
        {
            var categoryId = int.Parse(callbackData);

            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            var category = await categoryService.GetCategoryByIdAsync(categoryId);

            if (category == null)
            {
                await botClient.SendMessage(chat.Id, "❌ Category not found.", cancellationToken: cancellationToken);
                return;
            }

            state.SelectedCategoryId = categoryId;
            state.SelectedCategoryName = category.Name;
            state.Step = SelectSubCategoryStep;

            logger.LogInformation("Category selected: {CategoryId} - {CategoryName}", categoryId, category.Name);
            await ShowSubCategoriesAsync(botClient, chat, state, cancellationToken);
        }
        else if (callbackName == CallbackSubCategoryPrefix)
        {
            var subCategoryId = int.Parse(callbackData);

            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            var subCategory = await categoryService.GetSubCategoryByIdAsync(subCategoryId);

            if (subCategory == null)
            {
                await botClient.SendMessage(chat.Id, "❌ Subcategory not found.", cancellationToken: cancellationToken);
                return;
            }

            state.SelectedSubCategoryId = subCategoryId;
            state.SelectedSubCategoryName = subCategory.Name;
            state.Step = AddDescriptionStep;

            logger.LogInformation("SubCategory selected: {SubCategoryId} - {SubCategoryName}", subCategoryId, subCategory.Name);
            await AskForDescriptionAsync(botClient, chat, state, cancellationToken);
        }
    }

    public override bool CanHandleTextInput(ConversationState state)
    {
        return state.Step is AddDescriptionStep or InsertAmountStep;
    }

    public override async Task HandleTextInputAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;
        var text = message.Text!;

        if (state.Step == AddDescriptionStep)
        {
            state.Description = text;
            state.Step = InsertAmountStep;

            logger.LogInformation("Description entered: {Description}", text);
            await AskForAmountAsync(botClient, chat, state, cancellationToken);
        }
        else if (state.Step == InsertAmountStep)
        {
            await HandleAmountInputAsync(botClient, chat, text, state, cancellationToken);
        }
    }

    public override bool CanHandleBack(ConversationState state) =>
        state.Step is SelectSubCategoryStep
            or AddDescriptionStep
            or InsertAmountStep;

    public override async Task<bool> HandleBackAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling back from step {Step}", state.Step);

        switch (state.Step)
        {
            case SelectSubCategoryStep:
                // Go back to category selection
                state.SelectedCategoryId = null;
                state.SelectedCategoryName = null;
                state.Step = SelectCategoryStep;
                await ShowCategoriesAsync(botClient, chat, state, cancellationToken);
                return true;

            case AddDescriptionStep:
                // Go back to subcategory selection
                state.SelectedSubCategoryId = null;
                state.SelectedSubCategoryName = null;
                state.Step = SelectSubCategoryStep;
                await ShowSubCategoriesAsync(botClient, chat, state, cancellationToken);
                return true;

            case InsertAmountStep:
                // Go back to description input
                state.Description = null;
                state.Step = AddDescriptionStep;
                await AskForDescriptionAsync(botClient, chat, state, cancellationToken);
                return true;

            default:
                // Not in a step we can handle, return to main menu
                return false;
        }
    }

    // ========== PRIVATE HELPER METHODS ==========

    private async Task ShowCategoriesAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
        var categories = await categoryService.GetAllCategoriesAsync();

        var buttons = categories
            .Select(c => new[] { Utils.Utils.ButtonWithCallbackdata(c.Name, CallbackCategoryPrefix, c.Id) })
            .ToList();

        buttons.Add([Utils.Utils.MainMenu]);

        var keyboard = new InlineKeyboardMarkup(buttons);
        var text = "📁 *Select a category:*";

        if (state.LastBotMessageId.HasValue)
        {
            try
            {
                await botClient.EditMessageText(
                    chatId: chat.Id,
                    messageId: state.LastBotMessageId.Value,
                    text: text,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
                return;
            }
            catch (ApiRequestException)
            {
                // Message can no longer be edited, sending a new one
            }
        }

        var msg = await botClient.SendMessage(
            chatId: chat.Id,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task ShowSubCategoriesAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
        var subCategories = await categoryService.GetSubCategoriesByCategoryIdAsync(state.SelectedCategoryId!.Value);

        var buttons = subCategories
            .Select(c => new[] { Utils.Utils.ButtonWithCallbackdata(c.Name, CallbackSubCategoryPrefix, c.Id) })
            .ToList();

        buttons.Add([Utils.Utils.Back]);

        var keyboard = new InlineKeyboardMarkup(buttons);
        var text = $"📁 *{state.SelectedCategoryName}*\n\n📂 Select a subcategory:";

        try
        {
            await botClient.EditMessageText(
                chatId: chat.Id,
                messageId: state.LastBotMessageId!.Value,
                text: text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException)
        {
            var msg = await botClient.SendMessage(
                chatId: chat.Id,
                text: text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
            state.LastBotMessageId = msg.MessageId;
        }
    }

    private async Task AskForDescriptionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.Back],
            [Utils.Utils.MainMenu]
        ]);

        var text = $"📁 *{state.SelectedCategoryName}* > *{state.SelectedSubCategoryName}*\n\n" +
                   "📝 Enter a description for the expense:";

        try
        {
            await botClient.EditMessageText(
                chatId: chat.Id,
                messageId: state.LastBotMessageId!.Value,
                text: text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException)
        {
            var msg = await botClient.SendMessage(
                chatId: chat.Id,
                text: text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
            state.LastBotMessageId = msg.MessageId;
        }
    }

    private async Task AskForAmountAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.Back],
            [Utils.Utils.MainMenu]
        ]);

        var text = $"📁 *{state.SelectedCategoryName}* > *{state.SelectedSubCategoryName}*\n" +
                   $"📝 {state.Description}\n\n" +
                   "💰 Enter the amount (e.g., 12.50):";

        try
        {
            await botClient.EditMessageText(
                chatId: chat.Id,
                messageId: state.LastBotMessageId!.Value,
                text: text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException)
        {
            var msg = await botClient.SendMessage(
                chatId: chat.Id,
                text: text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
            state.LastBotMessageId = msg.MessageId;
        }
    }

    private async Task HandleAmountInputAsync(
        ITelegramBotClient botClient,
        Chat chat,
        string amountText,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        // Normalize input (replace comma with dot)
        var normalizedAmount = amountText.Replace(",", ".");

        if (!decimal.TryParse(normalizedAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            var message = await botClient.SendMessage(
                chatId: chat.Id,
                text: "❌ Invalid amount. Enter a positive number (e.g., 12.50):",
                cancellationToken: cancellationToken);
            state.LastBotMessageId = message.Id;
            return;
        }

        // Save the expense
        try
        {
            using var scope = scopeFactory.CreateScope();
            var expenseService = scope.ServiceProvider.GetRequiredService<ExpenseService>();

            await expenseService.CreateExpenseAsync(
                subCategoryId: state.SelectedSubCategoryId!.Value,
                amount: amount,
                description: state.Description ?? throw new InvalidOperationException("Description cannot be null"),
                notes: null,
                performedBy: chat.Username ?? chat.Id.ToString(),
                tags: []);

            logger.LogInformation("Expense created: {Amount} - {Description}", amount, state.Description);

            // Update message with confirmation
            await botClient.EditMessageText(
                chatId: chat.Id,
                messageId: state.LastBotMessageId!.Value,
                text: $"✅ *Expense successfully recorded!*\n\n" +
                      $"📁 {state.SelectedCategoryName} > {state.SelectedSubCategoryName}\n" +
                      $"📝 {state.Description}\n" +
                      $"💰 €{amount:F2}",
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup([
                    [Utils.Utils.MainMenu]
                ]),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating expense");
            await botClient.SendMessage(
                chatId: chat.Id,
                text: "❌ An error occurred while saving the expense. Please try again.",
                cancellationToken: cancellationToken);
        }
    }
}