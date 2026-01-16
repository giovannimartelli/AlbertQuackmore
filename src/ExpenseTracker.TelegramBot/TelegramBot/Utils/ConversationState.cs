namespace ExpenseTracker.TelegramBot.TelegramBot.Utils;

public class ConversationState
{
    public string Step { get; set; } = Utils.MainMenuStep;
    public int? SelectedCategoryId { get; set; }
    public string? SelectedCategoryName { get; set; }
    public int? SelectedSubCategoryId { get; set; }
    public string? SelectedSubCategoryName { get; set; }
    public int? SelectedTagId { get; set; }
    public string? SelectedTagName { get; set; }
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly? SelectedDate { get; set; }
    public int? LastBotMessageId { get; set; }
    
    /// <summary>
    /// Stores the ID of the main menu message to prevent it from being edited or deleted by sub-flows.
    /// </summary>
    public int? MainMenuMessageId { get; set; }
    
    /// <summary>
    /// Stores the message IDs of all messages sent during a sub-flow that are candidates for deletion.
    /// </summary>
    public List<int> FlowMessageIds { get; set; } = new();

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
        SelectedTagId = null;
        SelectedTagName = null;
        Description = null;
        Amount = null;
        SelectedDate = null;
        LastBotMessageId = null;
        CreatedSubCategoryId = null;
        CreatedSubCategoryName = null;
        // Note: MainMenuMessageId and FlowMessageIds are NOT reset here
        // They are managed separately by the flow cleanup logic
    }
    
    /// <summary>
    /// Tracks a message ID for later deletion when returning to main menu.
    /// </summary>
    public void TrackFlowMessage(int messageId)
    {
        if (!FlowMessageIds.Contains(messageId))
        {
            FlowMessageIds.Add(messageId);
        }
    }
    
    /// <summary>
    /// Clears all tracked flow message IDs after they have been deleted.
    /// </summary>
    public void ClearFlowMessages()
    {
        FlowMessageIds.Clear();
    }
    
    /// <summary>
    /// Resets the LastBotMessageId to point to the main menu message.
    /// </summary>
    public void ResetToMainMenu()
    {
        LastBotMessageId = MainMenuMessageId;
    }
}