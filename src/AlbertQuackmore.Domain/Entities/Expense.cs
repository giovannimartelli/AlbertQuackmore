namespace AlbertQuackmore.Domain.Entities;

public class Expense
{
    #region Properties

    public int Id { get; set; }

    public required decimal Amount { get; set; }
    public required int SubCategoryId { get; set; }
    public required DateOnly CreatedAt { get; set; }
    public required string Description { get; set; }
    public required string? Notes { get; set; }
    public int? TagId { get; set; }
    public required string PerformedBy { get; set; }

    #endregion

    #region Navigation Properties

    public virtual SubCategory SubCategory { get; set; } = null!;
    public virtual Tag? Tag { get; set; }

    #endregion
}