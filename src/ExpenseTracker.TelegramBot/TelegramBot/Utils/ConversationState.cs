namespace ExpenseTracker.TelegramBot.TelegramBot.Utils;

public class ConversationState
{
    public string Step { get; set; } = Utils.MainMenuStep;
    public int? SelectedCategoryId { get; set; }
    public string? SelectedCategoryName { get; set; }
    public int? SelectedSubCategoryId { get; set; }
    public string? SelectedSubCategoryName { get; set; }
    public string? Description { get; set; }
    public int? LastBotMessageId { get; set; }

    // Used for tag creation flow after subcategory creation
    public int? CreatedSubCategoryId { get; set; }
    public string? CreatedSubCategoryName { get; set; }

    public void Reset()
    {
        Step = Utils.MainMenuStep;
        SelectedCategoryId = null;
        SelectedCategoryName = null;
        SelectedSubCategoryId = null;
        SelectedSubCategoryName = null;
        Description = null;
        LastBotMessageId = null;
        CreatedSubCategoryId = null;
        CreatedSubCategoryName = null;
    }
}