using System.Globalization;
using ExpenseTracker.Services;
using ExpenseTracker.TelegramBot.TelegramBot.Utils;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExpenseTracker.TelegramBot.TelegramBot.Flows;

/// <summary>
/// Handles the flow for inserting a new expense.
/// Flow: Menu → Category → Subcategory → Tag → Description → Amount → Date → Save
/// </summary>
public class InsertExpenseFlowHandler(
    IServiceScopeFactory scopeFactory,
    IOptions<WebAppOptions> webAppOptions,
    ILogger<InsertExpenseFlowHandler> logger) : FlowHandler
{
    private readonly WebAppOptions _webAppOptions = webAppOptions.Value;

    private const string MenuCommandText = "💰 Inserisci spesa";

    private const string CallbackCategoryPrefix = "addexpenses_cat";
    private const string CallbackSubCategoryPrefix = "addexpenses_sub";
    private const string CallbackTagPrefix = "addexpenses_tag";
    private const string CallbackSkipTag = "addexpenses_skiptag";

    // Date selection buttons (ReplyKeyboard - text messages)
    private const string ButtonUseTodayDate = "📅 Usa data di oggi";
    private const string ButtonChooseDate = "📆 Scegli altra data";
    private const string ButtonBack = "◀️ Indietro";
    private const string ButtonMainMenu = "🏠 Menu principale";

    private const string SelectCategoryStep = "AddExpense_SelectCategory";
    private const string SelectSubCategoryStep = "AddExpense_SelectSubCategory";
    private const string SelectTagStep = "AddExpense_SelectTag";
    private const string AddDescriptionStep = "AddExpense_AddDescription";
    private const string InsertAmountStep = "AddExpense_InsertAmount";
    private const string SelectDateStep = "AddExpense_SelectDate";

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
            SelectTagStep => callbackName is CallbackTagPrefix or CallbackSkipTag,
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

            logger.LogInformation("SubCategory selected: {SubCategoryId} - {SubCategoryName}", subCategoryId, subCategory.Name);

            // Check if subcategory has tags
            var tags = await categoryService.GetTagsBySubCategoryIdAsync(subCategoryId);
            if (tags.Count > 0)
            {
                state.Step = SelectTagStep;
                await ShowTagsAsync(botClient, chat, state, tags, cancellationToken);
            }
            else
            {
                state.Step = AddDescriptionStep;
                await AskForDescriptionAsync(botClient, chat, state, cancellationToken);
            }
        }
        else if (callbackName == CallbackTagPrefix)
        {
            var tagId = int.Parse(callbackData);

            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            var tag = await categoryService.GetTagByIdAsync(tagId);

            if (tag == null)
            {
                await botClient.SendMessage(chat.Id, "❌ Tag not found.", cancellationToken: cancellationToken);
                return;
            }

            state.SelectedTagId = tagId;
            state.SelectedTagName = tag.Name;
            state.Step = AddDescriptionStep;

            logger.LogInformation("Tag selected: {TagId} - {TagName}", tagId, tag.Name);
            await AskForDescriptionAsync(botClient, chat, state, cancellationToken);
        }
        else if (callbackName == CallbackSkipTag)
        {
            state.SelectedTagId = null;
            state.SelectedTagName = null;
            state.Step = AddDescriptionStep;

            logger.LogInformation("Tag skipped");
            await AskForDescriptionAsync(botClient, chat, state, cancellationToken);
        }
    }

    public override bool CanHandleTextInput(ConversationState state)
    {
        return state.Step is AddDescriptionStep or InsertAmountStep or SelectDateStep;
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
        else if (state.Step == SelectDateStep)
        {
            await HandleDateSelectionAsync(botClient, chat, text, state, cancellationToken);
        }
    }

    private async Task HandleDateSelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        string text,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        if (text == ButtonUseTodayDate)
        {
            state.SelectedDate = DateOnly.FromDateTime(DateTime.UtcNow);
            logger.LogInformation("Using today's date: {Date}", state.SelectedDate);
            await SaveExpenseAsync(botClient, chat, state, cancellationToken);
        }
        else if (text == ButtonBack)
        {
            state.Amount = null;
            state.Step = InsertAmountStep;
            logger.LogInformation("Going back to amount input");
            // await RemoveReplyKeyboardAsync(botClient, chat, cancellationToken);
            await AskForAmountAsync(botClient, chat, state, cancellationToken);
        }
        else if (text == ButtonMainMenu)
        {
            // Reset state and show main menu keyboard
            state.Reset();
            logger.LogInformation("Returning to main menu");
            await ShowMainMenuMessageAsync(botClient, chat, cancellationToken);
        }
        // Note: ButtonChooseDate opens WebApp, handled via HandleWebAppDataAsync
    }

    public override bool CanHandleBack(ConversationState state) =>
        state.Step is SelectSubCategoryStep
            or SelectTagStep
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

            case SelectTagStep:
                // Go back to subcategory selection
                state.SelectedSubCategoryId = null;
                state.SelectedSubCategoryName = null;
                state.Step = SelectSubCategoryStep;
                await ShowSubCategoriesAsync(botClient, chat, state, cancellationToken);
                return true;

            case AddDescriptionStep:
                // Go back to tag selection if there were tags, otherwise to subcategory
                state.SelectedTagId = null;
                state.SelectedTagName = null;
                using (var scope = scopeFactory.CreateScope())
                {
                    var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
                    var tags = await categoryService.GetTagsBySubCategoryIdAsync(state.SelectedSubCategoryId!.Value);
                    if (tags.Count > 0)
                    {
                        state.Step = SelectTagStep;
                        await ShowTagsAsync(botClient, chat, state, tags, cancellationToken);
                        return true;
                    }
                }

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

    private async Task RemoveReplyKeyboardAsync(
        ITelegramBotClient botClient,
        Chat chat,
        CancellationToken cancellationToken)
    {
        try
        {
            var msg = await botClient.SendMessage(
                chatId: chat.Id,
                text: "⏳",
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
            await botClient.DeleteMessage(chat.Id, msg.MessageId, cancellationToken);
        }
        catch
        {
            // Ignore errors
        }
    }

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

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            text,
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

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

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            text,
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task ShowTagsAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        List<Domain.Entities.Tag> tags,
        CancellationToken cancellationToken)
    {
        var buttons = tags
            .Select(t => new[] { Utils.Utils.ButtonWithCallbackdata($"🏷️ {t.Name}", CallbackTagPrefix, t.Id) })
            .ToList();

        buttons.Add([Utils.Utils.ButtonWithCallbackdata("⏭️ Salta", CallbackSkipTag, "skip")]);
        buttons.Add([Utils.Utils.Back]);

        var keyboard = new InlineKeyboardMarkup(buttons);
        var text = $"📁 *{state.SelectedCategoryName}* > *{state.SelectedSubCategoryName}*\n\n🏷️ Seleziona un tag:";

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            text,
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
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

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            text,
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
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

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            text,
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
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

        state.Amount = amount;
        state.Step = SelectDateStep;

        logger.LogInformation("Amount entered: {Amount}", amount);
        await AskForDateAsync(botClient, chat, state, cancellationToken);
    }

    private async Task AskForDateAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        // All buttons in ReplyKeyboard
        var replyKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton(ButtonUseTodayDate) },
            new[] { KeyboardButton.WithWebApp(ButtonChooseDate, new WebAppInfo { Url = _webAppOptions.DatePickerUrl }) },
            new[] { new KeyboardButton(ButtonBack), new KeyboardButton(ButtonMainMenu) }
        })
        {
            ResizeKeyboard = true
        };

        var tagInfo = state.SelectedTagName != null ? $"\n🏷️ {state.SelectedTagName}" : "";
        var text = $"📁 *{state.SelectedCategoryName}* > *{state.SelectedSubCategoryName}*{tagInfo}\n" +
                   $"📝 {state.Description}\n" +
                   $"💰 €{state.Amount:F2}\n\n" +
                   "📆 *Seleziona la data della spesa:*";

        var msg = await botClient.SendMessage(
            chatId: chat.Id,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyKeyboard,
            cancellationToken: cancellationToken);

        state.TrackFlowMessage(msg.MessageId);
        state.LastBotMessageId = msg.MessageId;
    }

    public override bool CanHandleWebAppData(ConversationState state) =>
        state.Step == SelectDateStep;

    public override async Task HandleWebAppDataAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;
        var dateString = message.WebAppData!.Data;

        if (!DateOnly.TryParse(dateString, out var selectedDate))
        {
            logger.LogWarning("Invalid date received from WebApp: {DateString}", dateString);
            await botClient.SendFlowMessageAsync(
                chatId: chat.Id,
                state,
                text: "❌ Data non valida ricevuta. Riprova.",
                cancellationToken: cancellationToken);
            return;
        }

        state.SelectedDate = selectedDate;
        logger.LogInformation("Date selected from WebApp: {Date}", selectedDate);
        await SaveExpenseAsync(botClient, chat, state, cancellationToken);
    }

    private async Task SaveExpenseAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var expenseService = scope.ServiceProvider.GetRequiredService<ExpenseService>();

            await expenseService.CreateExpenseAsync(
                subCategoryId: state.SelectedSubCategoryId!.Value,
                amount: state.Amount!.Value,
                description: state.Description ?? throw new InvalidOperationException("Description cannot be null"),
                notes: null,
                performedBy: chat.Username ?? chat.Id.ToString(),
                tagId: state.SelectedTagId,
                state.SelectedDate!.Value);

            logger.LogInformation("Expense created: {Amount} - {Description} - Tag: {TagId} - Date: {Date}",
                state.Amount, state.Description, state.SelectedTagId, state.SelectedDate);

            // Build confirmation message with main menu keyboard
            var tagInfo = state.SelectedTagName != null ? $"\n🏷️ {state.SelectedTagName}" : "";
            var confirmationText = $"✅ *Spesa registrata!*\n\n" +
                                   $"📁 {state.SelectedCategoryName} > {state.SelectedSubCategoryName}{tagInfo}\n" +
                                   $"📝 {state.Description}\n" +
                                   $"💰 €{state.Amount:F2}\n" +
                                   $"📆 {state.SelectedDate:dd/MM/yyyy}";

            // Send confirmation with main menu keyboard
            await botClient.SendFlowMessageAsync(
                chatId: chat.Id,
                state,
                text: confirmationText,
                cancellationToken: cancellationToken);
            await ShowMainMenuMessageAsFlowMessageAsync(botClient, state, chat, cancellationToken);
            // Reset state
            state.Reset();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating expense");
            await botClient.SendMessage(
                chatId: chat.Id,
                text: "❌ Si è verificato un errore durante il salvataggio. Riprova.",
                replyMarkup: GetMainMenuKeyboard(),
                cancellationToken: cancellationToken);
        }
    }

    private async Task ShowMainMenuMessageAsync(
        ITelegramBotClient botClient,
        Chat chat,
        CancellationToken cancellationToken)
    {
        await botClient.SendMessage(
            chatId: chat.Id,
            text: "👋 *Menu principale*\n\nScegli un'operazione:",
            parseMode: ParseMode.Markdown,
            replyMarkup: GetMainMenuKeyboard(),
            cancellationToken: cancellationToken);
    }
    private async Task ShowMainMenuMessageAsFlowMessageAsync(
        ITelegramBotClient botClient,
        ConversationState state,
        Chat chat,
        CancellationToken cancellationToken)
    {
        await botClient.SendFlowMessageAsync(
            chatId: chat.Id,
            state,
            text: "👋 *Menu principale*\n\nScegli un'operazione:",
            parseMode: ParseMode.Markdown,
            replyMarkup: GetMainMenuKeyboard(),
            cancellationToken: cancellationToken);
    }

    private static ReplyKeyboardMarkup GetMainMenuKeyboard()
    {
        return new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton(MenuCommandText) },
            new[] { new KeyboardButton("⚙️ Settings") }
        })
        {
            ResizeKeyboard = true
        };
    }
}