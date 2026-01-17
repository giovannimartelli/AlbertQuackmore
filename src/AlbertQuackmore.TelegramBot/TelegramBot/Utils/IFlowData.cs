namespace AlbertQuackmore.TelegramBot.TelegramBot.Utils;

/// <summary>
/// Base interface for flow-specific data.
/// Each flow (InsertExpense, Settings, etc.) has its own implementation.
/// </summary>
public interface IFlowData
{
    /// <summary>
    /// The current step within this flow.
    /// </summary>
    string CurrentStep { get; set; }
}
