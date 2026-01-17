namespace AlbertQuackmore.Domain.Entities;

public class Budget
{
    #region Properties

    public int Id { get; set; }

    public required int Year { get; set; }
    public required int Month { get; set; }
    public required int SubCategoryId { get; set; }
    public required decimal Amount { get; set; }

    #endregion

    #region Navigation properties

    public virtual SubCategory SubCategory { get; set; } = null!;

    #endregion
}