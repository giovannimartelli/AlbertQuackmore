using AlbertQuackmore.TelegramBot.TelegramBot.Utils;

namespace AlbertQuackmore.TelegramBot.TelegramBot.Flows;

/// <summary>
/// Data for the insert expense flow.
/// </summary>
public class InsertExpenseFlowData : IFlowData
{
    public const string StepSelectCategory = "SelectCategory";
    public const string StepSelectSubCategory = "SelectSubCategory";
    public const string StepSelectTag = "SelectTag";
    public const string StepAddDescription = "AddDescription";
    public const string StepInsertAmount = "InsertAmount";
    public const string StepSelectDate = "SelectDate";

    public string CurrentStep { get; set; } = StepSelectCategory;

    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }

    public int? SubCategoryId { get; set; }
    public string? SubCategoryName { get; set; }

    public int? TagId { get; set; }
    public string? TagName { get; set; }

    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly? SelectedDate { get; set; }
}
