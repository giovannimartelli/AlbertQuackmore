using AlbertQuackmore.TelegramBot.TelegramBot.Utils;

namespace AlbertQuackmore.TelegramBot.TelegramBot.Flows;

/// <summary>
/// Data for the settings root flow.
/// </summary>
public class SettingsFlowData : IFlowData
{
    public const string StepRoot = "Root";

    public string CurrentStep { get; set; } = StepRoot;
}

/// <summary>
/// Data for the settings expenses sub-flow.
/// </summary>
public class SettingsExpensesFlowData : IFlowData
{
    public const string StepSelectAction = "SelectAction";
    public const string StepAddCategory = "AddCategory";
    public const string StepSelectCategoryForSub = "SelectCategoryForSub";
    public const string StepAddSubCategory = "AddSubCategory";
    public const string StepAskAddTag = "AskAddTag";
    public const string StepAddTag = "AddTag";

    public string CurrentStep { get; set; } = StepSelectAction;

    /// <summary>
    /// Selected category (for creating subcategories).
    /// </summary>
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }

    /// <summary>
    /// Created subcategory (for adding tags).
    /// </summary>
    public int? CreatedSubCategoryId { get; set; }
    public string? CreatedSubCategoryName { get; set; }
}
