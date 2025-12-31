using System.Collections.Concurrent;
using System.Globalization;
using ExpenseTracker.Services;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExpenseTracker.TelegramBot;

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
    private const string BtnBack = "⬅️ Indietro";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message],
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

        try
        {
            // Gestione comando /start
            if (messageText.StartsWith("/start"))
            {
                await ShowMainMenuAsync(botClient, chat, cancellationToken);
                return;
            }

            var state = GetOrCreateState(chatUsername);

            // Gestione pulsante Indietro
            if (messageText == BtnBack)
            {
                await HandleBackButtonAsync(botClient, chat, state, cancellationToken);
                return;
            }

            // Gestione in base allo stato corrente
            switch (state.Step)
            {
                case ConversationStep.MainMenu:
                    await HandleMainMenuSelectionAsync(botClient, chat, messageText, state, cancellationToken);
                    break;

                case ConversationStep.SelectCategory:
                    await HandleCategorySelectionAsync(botClient, chat, messageText, state, cancellationToken);
                    break;

                case ConversationStep.SelectSubCategory:
                    await HandleSubCategorySelectionAsync(botClient, chat, messageText, state, cancellationToken);
                    break;

                case ConversationStep.EnterDescription:
                    await HandleDescriptionInputAsync(botClient, chat, messageText, state, cancellationToken);
                    break;

                case ConversationStep.EnterAmount:
                    await HandleAmountInputAsync(botClient, chat, messageText, state, cancellationToken);
                    break;

                default:
                    await ShowMainMenuAsync(botClient, chat, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling message from {ChatId}", chatUsername);
            await botClient.SendMessage(
                chatId: chatUsername,
                text: "Si è verificato un errore. Riprova più tardi.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task ShowMainMenuAsync(ITelegramBotClient botClient, Chat chat, CancellationToken cancellationToken)
    {
        var state = GetOrCreateState(chat.Username!);
        state.Reset();

        var keyboard = new ReplyKeyboardMarkup([
            [BtnInsertExpense],
            [BtnDownloadExcel]
        ])
        {
            ResizeKeyboard = true
        };

        await botClient.SendMessage(
            chatId: chat.Id,
            text: "Benvenuto! Cosa vuoi fare?",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
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
                await botClient.SendMessage(
                    chatId: chat.Id,
                    text: "Funzionalità non ancora implementata.",
                    cancellationToken: cancellationToken);
                break;

            default:
                await botClient.SendMessage(
                    chatId: chat.Id,
                    text: "Seleziona un'opzione dal menu.",
                    cancellationToken: cancellationToken);
                break;
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
            await botClient.SendMessage(
                chatId: chat.Id,
                text: "Non ci sono categorie. Creane una prima.",
                cancellationToken: cancellationToken);
            return;
        }

        state.Step = ConversationStep.SelectCategory;

        // Crea la keyboard con le categorie (2 per riga) + pulsante indietro
        var keyboardRows = new List<KeyboardButton[]>();

        for (var i = 0; i < categories.Count; i += 2)
        {
            if (i + 1 < categories.Count)
            {
                keyboardRows.Add([new KeyboardButton(categories[i].Name), new KeyboardButton(categories[i + 1].Name)]);
            }
            else
            {
                keyboardRows.Add([new KeyboardButton(categories[i].Name)]);
            }
        }

        keyboardRows.Add([new KeyboardButton(BtnBack)]);

        var keyboard = new ReplyKeyboardMarkup(keyboardRows) { ResizeKeyboard = true };

        await botClient.SendMessage(
            chatId: chat.Id,
            text: "Seleziona una categoria:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleCategorySelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        string messageText,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();

        var category = await categoryService.GetCategoryByNameAsync(messageText);

        if (category == null)
        {
            await botClient.SendMessage(
                chatId: chat.Id,
                text: "Categoria non trovata. Seleziona una categoria dalla lista.",
                cancellationToken: cancellationToken);
            return;
        }

        state.SelectedCategoryId = category.Id;
        state.SelectedCategoryName = category.Name;

        await ShowSubCategoriesAsync(botClient, chat, category.Id, state, cancellationToken);
    }

    private async Task ShowSubCategoriesAsync(
        ITelegramBotClient botClient,
        Chat chat,
        int categoryId,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();

        var subCategories = await categoryService.GetSubCategoriesByCategoryIdAsync(categoryId);

        if (subCategories.Count == 0)
        {
            await botClient.SendMessage(
                chatId: chat.Id,
                text: $"Non ci sono sottocategorie per '{state.SelectedCategoryName}'. Creane una prima.",
                cancellationToken: cancellationToken);
            state.Step = ConversationStep.SelectCategory;
            return;
        }

        state.Step = ConversationStep.SelectSubCategory;

        // Crea la keyboard con le sottocategorie (2 per riga) + pulsante indietro
        var keyboardRows = new List<KeyboardButton[]>();

        for (var i = 0; i < subCategories.Count; i += 2)
        {
            if (i + 1 < subCategories.Count)
            {
                keyboardRows.Add([new KeyboardButton(subCategories[i].Name), new KeyboardButton(subCategories[i + 1].Name)]);
            }
            else
            {
                keyboardRows.Add([new KeyboardButton(subCategories[i].Name)]);
            }
        }

        keyboardRows.Add([new KeyboardButton(BtnBack)]);

        var keyboard = new ReplyKeyboardMarkup(keyboardRows) { ResizeKeyboard = true };

        await botClient.SendMessage(
            chatId: chat.Id,
            text: $"Categoria: {state.SelectedCategoryName}\nSeleziona una sottocategoria:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleSubCategorySelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        string messageText,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();

        var subCategories = await categoryService.GetSubCategoriesByCategoryIdAsync(state.SelectedCategoryId!.Value);
        var subCategory = subCategories.FirstOrDefault(sc => sc.Name.Equals(messageText, StringComparison.OrdinalIgnoreCase));

        if (subCategory == null)
        {
            await botClient.SendMessage(
                chatId: chat.Id,
                text: "Sottocategoria non trovata. Seleziona una sottocategoria dalla lista.",
                cancellationToken: cancellationToken);
            return;
        }

        state.SelectedSubCategoryId = subCategory.Id;
        state.SelectedSubCategoryName = subCategory.Name;
        state.Step = ConversationStep.EnterDescription;

        // Rimuovi keyboard e chiedi la descrizione
        var keyboard = new ReplyKeyboardMarkup([[new KeyboardButton(BtnBack)]])
        {
            ResizeKeyboard = true
        };

        await botClient.SendMessage(
            chatId: chat.Id,
            text: $"Categoria: {state.SelectedCategoryName} > {state.SelectedSubCategoryName}\n\nInserisci una descrizione per la spesa:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
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

        // Usa ForceReply per suggerire la tastiera nativa (possibilmente numerica su mobile)
        var forceReply = new ForceReplyMarkup
        {
            InputFieldPlaceholder = "Inserisci l'importo (es. 12.50)",
            Selective = true
        };

        await botClient.SendMessage(
            chatId: chat.Id,
            text: $"Categoria: {state.SelectedCategoryName} > {state.SelectedSubCategoryName}\n" +
                  $"Descrizione: {state.Description}\n\n" +
                  "Inserisci l'importo (es. 12.50):",
            replyMarkup: forceReply,
            cancellationToken: cancellationToken);
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

        await botClient.SendMessage(
            chatId: chat.Id,
            text: $"✅ Spesa registrata!\n\n" +
                  $"📁 {state.SelectedCategoryName} > {state.SelectedSubCategoryName}\n" +
                  $"📝 {state.Description}\n" +
                  $"💰 €{amount:F2}",
            cancellationToken: cancellationToken);

        // Torna al menu principale
        await ShowMainMenuAsync(botClient, chat, cancellationToken);
    }

    private async Task HandleBackButtonAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        switch (state.Step)
        {
            case ConversationStep.SelectCategory:
                await ShowMainMenuAsync(botClient, chat, cancellationToken);
                break;

            case ConversationStep.SelectSubCategory:
                state.SelectedCategoryId = null;
                state.SelectedCategoryName = null;
                await ShowCategoriesAsync(botClient, chat, state, cancellationToken);
                break;

            case ConversationStep.EnterDescription:
                await ShowSubCategoriesAsync(botClient, chat, state.SelectedCategoryId!.Value, state, cancellationToken);
                break;

            case ConversationStep.EnterAmount:
                state.Description = null;
                state.Step = ConversationStep.EnterDescription;

                var keyboard = new ReplyKeyboardMarkup(new[] { new[] { new KeyboardButton(BtnBack) } })
                {
                    ResizeKeyboard = true
                };

                await botClient.SendMessage(
                    chatId: chat.Id,
                    text: $"Categoria: {state.SelectedCategoryName} > {state.SelectedSubCategoryName}\n\nInserisci una descrizione per la spesa:",
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
                break;

            default:
                await ShowMainMenuAsync(botClient, chat, cancellationToken);
                break;
        }
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