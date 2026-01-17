namespace AlbertQuackmore.TelegramBot.TelegramBot.Utils;

/// <summary>
/// Holds UI infrastructure and the current flow's data.
/// Flow-specific data is stored in FlowData property.
/// </summary>
public class ConversationState
{
    /// <summary>
    /// Current flow's data. Null when at main menu.
    /// </summary>
    public IFlowData? FlowData { get; set; }

    /// <summary>
    /// ID of the last message sent by the bot (for editing).
    /// </summary>
    public int? LastBotMessageId { get; set; }

    /// <summary>
    /// ID of the main menu message to prevent it from being edited or deleted by sub-flows.
    /// </summary>
    public int? MainMenuMessageId { get; set; }

    /// <summary>
    /// Message IDs of all messages sent during a sub-flow that are candidates for deletion.
    /// </summary>
    public List<int> FlowMessageIds { get; set; } = new();

    /// <summary>
    /// Gets the current step. Returns MainMenuStep if no flow is active.
    /// </summary>
    public string CurrentStep => FlowData?.CurrentStep ?? Utils.MainMenuStep;

    /// <summary>
    /// Gets the flow data cast to the specified type.
    /// Returns null if FlowData is null or not of the expected type.
    /// </summary>
    public T? GetFlowData<T>() where T : class, IFlowData => FlowData as T;

    /// <summary>
    /// Sets the flow data, replacing any existing data.
    /// </summary>
    public void SetFlowData<T>(T data) where T : class, IFlowData => FlowData = data;

    /// <summary>
    /// Resets the flow data to null (returns to main menu state).
    /// Does NOT reset UI infrastructure (LastBotMessageId, MainMenuMessageId, FlowMessageIds).
    /// </summary>
    public void Reset()
    {
        FlowData = null;
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
