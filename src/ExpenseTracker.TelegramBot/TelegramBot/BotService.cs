using System.Collections.Concurrent;
using System.Globalization;
using ExpenseTracker.Services;
using ExpenseTracker.TelegramBot.TelegramBot.Utils;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExpenseTracker.TelegramBot.TelegramBot;

public class BotService(
    ITelegramBotClient bClient,
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramOptions> options,
    ILogger<BotService> logger)
    : BackgroundService
{
    private readonly TelegramOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, ConversationState> _conversationStates = new();
    private const string BtnInsertExpense = "💰 Inserisci spesa";
    private const string BtnDownloadExcel = "📊 Download Excel";
    private static readonly List<string> Operations = [BtnInsertExpense, BtnDownloadExcel];

    private static readonly ReplyKeyboardMarkup Keyboard = new([
        [BtnInsertExpense],
        [BtnDownloadExcel]
    ])
    {
        ResizeKeyboard = true,
        IsPersistent = true
    };

    // Prefissi per CallbackData
    private const string CallbackCategoryPrefix = "cat:";
    private const string CallbackSubCategoryPrefix = "sub:";
    private const string CallbackBack = "back";
    private const string CallbackMainMenu = "main";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
            DropPendingUpdates = true
        };
        logger.LogInformation("Starting Telegram bot polling...");
        await bClient.ReceiveAsync(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);
    }

    private ConversationState GetOrCreateState(string chatUsername)
    {
        return _conversationStates.GetOrAdd(chatUsername, _ => new ConversationState());
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.CallbackQuery is { } callbackQuery)

            await HandleCallbackQueryAsync(botClient, callbackQuery, cancellationToken);
        else
            await HandleTextMessageAsync(botClient, update, cancellationToken);
    }

    private async Task HandleTextMessageAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        if (update.Message is not { Text: { } messageText } message)
            return;

        var chat = message.Chat;
        var chatUsername = chat.Username;
        var chatId = chat.Id;

        if (chatUsername is null || (_options.AllowedUsername.Length > 0 && !_options.AllowedUsername.Contains(chatUsername)))
        {
            logger.LogWarning("Unauthorized access attempt from chat {ChatId}", chatUsername);
            await botClient.SendMessage(
                chatId: chatId,
                text: "Non sei autorizzato ad usare questo bot.",
                cancellationToken: cancellationToken);

            return;
        }

        logger.LogInformation("Received message from {ChatId}: {Message}", chatUsername, messageText);

        if (messageText.StartsWith("/start"))
        {
            await ShowMainMenuAsync(botClient, chat, true, cancellationToken);
            return;
        }

        var state = GetOrCreateState(chatUsername);

        if (Operations.Contains(messageText))
        {
            await DeleteLastBotMessage(botClient, chat, state, cancellationToken);
            state.Reset();
            await HandleMainMenuSelectionAsync(botClient, chat, messageText, state, cancellationToken);
        }
        else
            switch (state.Step)
            {
                case ConversationStep.EnterDescription:
                    await HandleDescriptionInputAsync(botClient, chat, messageText, state, cancellationToken);
                    break;

                case ConversationStep.EnterAmount:
                    await HandleAmountInputAsync(botClient, chat, messageText, state, cancellationToken);
                    break;
                case ConversationStep.MainMenu:
                case ConversationStep.SelectCategory:
                case ConversationStep.SelectSubCategory:
                default:
                    await DeleteLastBotMessage(botClient, chat, state, cancellationToken);
                    var msg = await botClient.SendMessage(
                        chatId: chat.Id,
                        text: "Hai rotto qualcosa, riparti dal menu principale",
                        cancellationToken: cancellationToken);
                    state.LastBotMessageId = msg.MessageId;
                    break;
            }

        await DeleteUserMessage(botClient, chat, message, cancellationToken);
    }

    private async Task HandleCallbackQueryAsync(
        ITelegramBotClient botClient,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        var chat = callbackQuery.Message?.Chat;
        var chatUsername = chat?.Username;
        var callbackData = callbackQuery.Data;

        if (chat is null || chatUsername is null || callbackData is null)
            return;

        if (_options.AllowedUsername.Length > 0 && !_options.AllowedUsername.Contains(chatUsername))
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Non autorizzato", cancellationToken: cancellationToken);
            return;
        }

        logger.LogInformation("Received callback from {ChatId}: {Data}", chatUsername, callbackData);

        var state = GetOrCreateState(chatUsername);
        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
        switch (callbackData)
        {
            case CallbackMainMenu:
                await ShowMainMenuAsync(botClient, chat, false, cancellationToken);
                break;
            case CallbackBack:
                await HandleInlineBackAsync(botClient, chat, state, cancellationToken);
                break;
            default:
            {
                var callback = callbackData.Split(":")[0] + ":";
                var data = callbackData.Split(":")[1];
                switch (callback)
                {
                    case CallbackCategoryPrefix:
                        if (int.TryParse(data, out var categoryId))
                        {
                            await HandleInlineCategorySelectionAsync(botClient, chat, categoryId, state, cancellationToken);
                        }

                        break;
                    case CallbackSubCategoryPrefix:
                        if (int.TryParse(data, out var subCategoryId))
                        {
                            await HandleInlineSubCategorySelectionAsync(botClient, chat, subCategoryId, state, cancellationToken);
                        }

                        break;
                }

                break;
            }
        }
    }

    private async Task ShowMainMenuAsync(ITelegramBotClient botClient, Chat chat, bool isCommand = false, CancellationToken cancellationToken = default)
    {
        var state = GetOrCreateState(chat.Username!);
        await DeleteLastBotMessage(botClient, chat, state, cancellationToken);
        state.Reset();
        if (isCommand)
        {
            await botClient.SendMessage(
                chatId: chat.Id,
                text: "Benvenuto! Cosa vuoi fare?",
                replyMarkup: Keyboard,
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleMainMenuSelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        string messageText,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        switch (messageText)
        {
            case BtnInsertExpense:
                await ShowCategoriesAsync(botClient, chat, state, cancellationToken);
                break;

            case BtnDownloadExcel:
                var message = await botClient.SendMessage(
                    chatId: chat.Id,
                    text: "Funzionalità non ancora implementata.",
                    cancellationToken: cancellationToken);
                state.LastBotMessageId = message.MessageId;
                break;

            default:
                throw new ArgumentException("Can't be there");
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

        if (categories.Count == 0)
        {
            var message = await botClient.SendMessage(
                chatId: chat.Id,
                text: "Non ci sono categorie. Creane una prima.",
                cancellationToken: cancellationToken);
            state.LastBotMessageId = message.MessageId;
            return;
        }

        state.Step = ConversationStep.SelectCategory;

        // Crea InlineKeyboard con le categorie (2 per riga) + pulsante menu
        var inlineRows = new List<InlineKeyboardButton[]>();

        for (var i = 0; i < categories.Count; i += 2)
        {
            if (i + 1 < categories.Count)
            {
                inlineRows.Add([
                    InlineKeyboardButton.WithCallbackData(categories[i].Name, $"{CallbackCategoryPrefix}{categories[i].Id}"),
                    InlineKeyboardButton.WithCallbackData(categories[i + 1].Name, $"{CallbackCategoryPrefix}{categories[i + 1].Id}")
                ]);
            }
            else
            {
                inlineRows.Add([
                    InlineKeyboardButton.WithCallbackData(categories[i].Name, $"{CallbackCategoryPrefix}{categories[i].Id}")
                ]);
            }
        }

        inlineRows.Add([InlineKeyboardButton.WithCallbackData("🏠 Menu principale", CallbackMainMenu)]);

        var inlineKeyboard = new InlineKeyboardMarkup(inlineRows);
        var text = "📁 *Seleziona una categoria:*";

        var sentMessage = await botClient.TryEditMessageText(
            chatId: chat.Id,
            messageId: state.LastBotMessageId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: inlineKeyboard,
            cancellationToken: cancellationToken);

        state.LastBotMessageId = sentMessage.MessageId;
    }

    private async Task HandleInlineCategorySelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        int categoryId,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();

        var category = await categoryService.GetCategoryByIdAsync(categoryId);

        if (category == null)
        {
            logger.LogWarning("Category {CategoryId} not found", categoryId);
            return;
        }

        state.SelectedCategoryId = category.Id;
        state.SelectedCategoryName = category.Name;

        await ShowSubCategoriesAsync(botClient, chat, state, cancellationToken);
    }

    private async Task ShowSubCategoriesAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();

        var subCategories = await categoryService.GetSubCategoriesByCategoryIdAsync(state.SelectedCategoryId ?? throw new ArgumentException());

        if (subCategories.Count == 0)
        {
            // Modifica il messaggio per mostrare l'errore e tornare indietro
            var errorText = $"⚠️ Non ci sono sottocategorie per '*{state.SelectedCategoryName}*'.\n\nTorna indietro e creane una.";
            var errorKeyboard = new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("⬅️ Indietro", CallbackBack)],
                [InlineKeyboardButton.WithCallbackData("🏠 Menu principale", CallbackMainMenu)]
            ]);
            var message = await botClient.TryEditMessageText(
                chatId: chat.Id,
                messageId: state.LastBotMessageId,
                text: errorText,
                parseMode: ParseMode.Markdown,
                replyMarkup: errorKeyboard,
                cancellationToken: cancellationToken);
            state.LastBotMessageId = message.Id;
            return;
        }

        state.Step = ConversationStep.SelectSubCategory;

        // Crea InlineKeyboard con le sottocategorie (2 per riga) + pulsante indietro
        var inlineRows = new List<InlineKeyboardButton[]>();

        for (var i = 0; i < subCategories.Count; i += 2)
        {
            if (i + 1 < subCategories.Count)
            {
                inlineRows.Add([
                    InlineKeyboardButton.WithCallbackData(subCategories[i].Name, $"{CallbackSubCategoryPrefix}{subCategories[i].Id}"),
                    InlineKeyboardButton.WithCallbackData(subCategories[i + 1].Name, $"{CallbackSubCategoryPrefix}{subCategories[i + 1].Id}")
                ]);
            }
            else
            {
                inlineRows.Add([
                    InlineKeyboardButton.WithCallbackData(subCategories[i].Name, $"{CallbackSubCategoryPrefix}{subCategories[i].Id}")
                ]);
            }
        }

        inlineRows.Add([InlineKeyboardButton.WithCallbackData("⬅️ Indietro", CallbackBack)]);

        var inlineKeyboard = new InlineKeyboardMarkup(inlineRows);
        var text = $"📁 *{state.SelectedCategoryName}*\n\nSeleziona una sottocategoria:";
        var sentMessage = await botClient.TryEditMessageText(
            chatId: chat.Id,
            messageId: state.LastBotMessageId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: inlineKeyboard,
            cancellationToken: cancellationToken);

        state.LastBotMessageId = sentMessage.MessageId;
    }

    private async Task HandleInlineSubCategorySelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        int subCategoryId,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();

        var subCategory = await categoryService.GetSubCategoryByIdAsync(subCategoryId);

        if (subCategory == null)
        {
            logger.LogWarning("SubCategory {SubCategoryId} not found", subCategoryId);
            return;
        }

        state.SelectedSubCategoryId = subCategory.Id;
        state.SelectedSubCategoryName = subCategory.Name;
        state.Step = ConversationStep.EnterDescription;

        var summaryText = $"✅ *Selezione completata*\n\n" +
                          $"📁 {state.SelectedCategoryName} > {state.SelectedSubCategoryName}\n\n" +
                          $"📝 Inserisci descrizione per la spesa: ";

        var message = await botClient.TryEditMessageText(
            chatId: chat.Id,
            messageId: state.LastBotMessageId!.Value,
            text: summaryText,
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup([[InlineKeyboardButton.WithCallbackData("⬅️ Indietro", CallbackBack)]]),
            cancellationToken: cancellationToken);
        state.LastBotMessageId = message.Id;
    }

    private async Task HandleDescriptionInputAsync(
        ITelegramBotClient botClient,
        Chat chat,
        string messageText,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        state.Description = messageText;
        state.Step = ConversationStep.EnterAmount;

        var message = await botClient.TryEditMessageText(
            chatId: chat.Id,
            messageId: state.LastBotMessageId!.Value,
            text: $"📁 {state.SelectedCategoryName} > {state.SelectedSubCategoryName}\n" +
                  $"📝 {state.Description}\n\n" +
                  "💰 Inserisci l'importo (es. 12.50):",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup([[InlineKeyboardButton.WithCallbackData("⬅️ Indietro", CallbackBack)]]),
            cancellationToken: cancellationToken);
        state.LastBotMessageId = message.Id;
    }

    private async Task HandleAmountInputAsync(
        ITelegramBotClient botClient,
        Chat chat,
        string messageText,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        // Prova a parsare l'importo
        var amountText = messageText.Replace(",", ".");

        if (!decimal.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            await botClient.SendMessage(
                chatId: chat.Id,
                text: "Importo non valido. Inserisci un numero positivo (es. 12.50):",
                cancellationToken: cancellationToken);
            return;
        }

        // Salva la spesa
        using var scope = scopeFactory.CreateScope();
        var expenseService = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        await expenseService.CreateExpenseAsync(state.SelectedSubCategoryId!.Value, amount, state.Description ?? throw new ArgumentException("Description Must be not null"), null, chat.Username!, []);

        await botClient.EditMessageText(
            chatId: chat.Id,
            messageId: state.LastBotMessageId!.Value,
            text: $"✅ Spesa registrata!\n\n" +
                  $"📁 {state.SelectedCategoryName} > {state.SelectedSubCategoryName}\n" +
                  $"📝 {state.Description}\n" +
                  $"💰 €{amount:F2}",
            cancellationToken: cancellationToken);
    }

    private async Task HandleInlineBackAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        switch (state.Step)
        {
            case ConversationStep.SelectSubCategory:
                state.SelectedCategoryId = null;
                state.SelectedCategoryName = null;
                await ShowCategoriesAsync(botClient, chat, state, cancellationToken);
                break;
            case ConversationStep.SelectCategory:
                await ShowMainMenuAsync(botClient, chat, false, cancellationToken);
                break;
            case ConversationStep.EnterDescription:
                state.SelectedSubCategoryId = null;
                state.SelectedSubCategoryName = null;
                await ShowSubCategoriesAsync(botClient, chat, state, cancellationToken);
                break;
            case ConversationStep.EnterAmount:
                state.Description = null;
                await HandleInlineSubCategorySelectionAsync(botClient, chat, state.SelectedSubCategoryId ?? -1, state, cancellationToken);
                break;
            case ConversationStep.MainMenu:
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task DeleteLastBotMessage(ITelegramBotClient botClient, Chat chat, ConversationState state, CancellationToken cancellationToken = default)
    {
        if (state.LastBotMessageId is not null)
            await botClient.DeleteMessage(chat.Id, state.LastBotMessageId.Value, cancellationToken);
        state.LastBotMessageId = null;
    }

    private async Task DeleteUserMessage(ITelegramBotClient botClient, Chat chat, Message message, CancellationToken cancellationToken = default)
    {
        await botClient.DeleteMessage(chat.Id, message.MessageId, cancellationToken);
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
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