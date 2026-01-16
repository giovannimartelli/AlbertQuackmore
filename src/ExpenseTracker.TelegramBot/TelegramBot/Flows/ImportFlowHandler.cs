using ExpenseTracker.Services;
using ExpenseTracker.TelegramBot.TelegramBot.Utils;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExpenseTracker.TelegramBot.TelegramBot.Flows;

public class ImportFlowData : IFlowData
{
    public const string StepWaitingForFile = "waiting_for_file";
    public const string StepWaitingForYear = "waiting_for_year";

    public string CurrentStep { get; set; } = StepWaitingForYear;
    public int? Year { get; set; }
}

public class ImportFlowHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<ImportFlowHandler> logger) : FlowHandler
{
    private const string MenuCommandText = "📥 Importa Excel";

    public override string GetMenuItemInfo() => MenuCommandText;
    public override bool CanHandleMenuCommand(string command) => command == MenuCommandText;

    public override async Task HandleMenuSelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting import flow for chat {ChatId}", chat.Id);

        var flowData = new ImportFlowData();
        state.SetFlowData(flowData);

        await AskForYearAsync(botClient, chat, state, cancellationToken);
    }

    public override bool CanHandleCallback(string callbackName, string callbackData, ConversationState state)
    {
        return false;
    }

    public override Task HandleCallbackAsync(
        ITelegramBotClient botClient,
        string callbackName,
        string callbackData,
        CallbackQuery callbackQuery,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override bool CanHandleTextInput(ConversationState state)
    {
        var flowData = state.GetFlowData<ImportFlowData>();
        return flowData?.CurrentStep == ImportFlowData.StepWaitingForYear;
    }

    public override async Task HandleTextInputAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;
        var text = message.Text!.Trim();
        var flowData = state.GetFlowData<ImportFlowData>()!;

        if (flowData.CurrentStep == ImportFlowData.StepWaitingForYear)
        {
            if (!int.TryParse(text, out var year) || year < 2020 || year > 2100)
            {
                await botClient.SendMessage(
                    chat.Id,
                    "❌ Anno non valido. Inserisci un anno tra 2020 e 2100:",
                    cancellationToken: cancellationToken);
                return;
            }

            flowData.Year = year;
            flowData.CurrentStep = ImportFlowData.StepWaitingForFile;

            await AskForFileAsync(botClient, chat, state, cancellationToken);
        }
    }

    public override bool CanHandleBack(ConversationState state)
    {
        var flowData = state.GetFlowData<ImportFlowData>();
        return flowData != null;
    }

    public override async Task<bool> HandleBackAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var flowData = state.GetFlowData<ImportFlowData>()!;

        if (flowData.CurrentStep == ImportFlowData.StepWaitingForFile)
        {
            flowData.Year = null;
            flowData.CurrentStep = ImportFlowData.StepWaitingForYear;
            await AskForYearAsync(botClient, chat, state, cancellationToken);
            return true;
        }

        return false;
    }

    public override bool CanHandleDocument(ConversationState state)
    {
        var flowData = state.GetFlowData<ImportFlowData>();
        return flowData?.CurrentStep == ImportFlowData.StepWaitingForFile && flowData.Year.HasValue;
    }

    public override async Task HandleDocumentAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;
        var document = message.Document!;
        var flowData = state.GetFlowData<ImportFlowData>()!;

        // Validate file type
        if (!document.FileName?.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ?? true)
        {
            await botClient.SendMessage(
                chat.Id,
                "❌ File non valido. Invia un file Excel (.xlsx)",
                cancellationToken: cancellationToken);
            return;
        }

        // Check file size (max 10MB)
        if (document.FileSize > 10 * 1024 * 1024)
        {
            await botClient.SendMessage(
                chat.Id,
                "❌ File troppo grande. Dimensione massima: 10MB",
                cancellationToken: cancellationToken);
            return;
        }

        var processingMsg = await botClient.SendMessage(
            chat.Id,
            "⏳ Elaborazione in corso...",
            cancellationToken: cancellationToken);

        try
        {
            // Download file
            var file = await botClient.GetFile(document.FileId, cancellationToken);
            using var stream = new MemoryStream();
            await botClient.DownloadFile(file.FilePath!, stream, cancellationToken);
            stream.Position = 0;

            // Import
            using var scope = scopeFactory.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<ImportService>();
            var result = await importService.ImportFromExcelAsync(stream, flowData.Year!.Value);

            // Build result message
            var resultText = $"✅ *Import completato per anno {flowData.Year}!*\n\n" +
                             $"📁 Categorie create: {result.CategoriesCreated}\n" +
                             $"📂 Sottocategorie create: {result.SubCategoriesCreated}\n" +
                             $"🏷️ Tag creati: {result.TagsCreated}\n" +
                             $"💰 Budget creati: {result.BudgetsCreated}";

            if (result.Warnings.Count > 0)
            {
                resultText += $"\n\n⚠️ *Avvisi:*\n" + string.Join("\n", result.Warnings.Take(10));
                if (result.Warnings.Count > 10)
                    resultText += $"\n... e altri {result.Warnings.Count - 10} avvisi";
            }

            if (result.Errors.Count > 0)
            {
                resultText += $"\n\n❌ *Errori:*\n" + string.Join("\n", result.Errors.Take(5));
                if (result.Errors.Count > 5)
                    resultText += $"\n... e altri {result.Errors.Count - 5} errori";
            }

            logger.LogInformation(
                "Import completed for year {Year}: {Categories} categories, {SubCategories} subcategories, {Tags} tags, {Budgets} budgets",
                flowData.Year, result.CategoriesCreated, result.SubCategoriesCreated, result.TagsCreated, result.BudgetsCreated);

            await botClient.DeleteMessage(chat.Id, processingMsg.MessageId, cancellationToken);
            await botClient.SendMessage(
                chat.Id,
                resultText,
                parseMode: ParseMode.Markdown,
                replyMarkup: GetMainMenuKeyboard(),
                cancellationToken: cancellationToken);

            state.Reset();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing import file");
            await botClient.DeleteMessage(chat.Id, processingMsg.MessageId, cancellationToken);
            await botClient.SendMessage(
                chat.Id,
                $"❌ Errore durante l'elaborazione: {ex.Message}",
                replyMarkup: GetMainMenuKeyboard(),
                cancellationToken: cancellationToken);
        }
    }

    private async Task AskForYearAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var currentYear = DateTime.Now.Year;
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.MainMenu]
        ]);

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            $"📅 *Importa Budget da Excel*\n\nInserisci l'anno per il budget (es. {currentYear}):",
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task AskForFileAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var flowData = state.GetFlowData<ImportFlowData>()!;
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.Back],
            [Utils.Utils.MainMenu]
        ]);

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            $"📤 *Anno selezionato: {flowData.Year}*\n\n" +
            "Invia il file Excel (.xlsx) con le categorie e i budget.\n\n" +
            "Il file deve avere le colonne:\n" +
            "• A: Categoria\n" +
            "• B: Sottocategoria\n" +
            "• D: Tags (separati da virgola)\n" +
            "• E: Budget Mensile",
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private static ReplyKeyboardMarkup GetMainMenuKeyboard()
    {
        return new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("💰 Inserisci spesa") },
            new[] { new KeyboardButton("📥 Importa Excel") },
            new[] { new KeyboardButton("⚙️ Settings") }
        })
        {
            ResizeKeyboard = true
        };
    }
}
