namespace AlbertQuackmore.Domain.Entities;

public class SubCategory
{
    #region Properties

    public int Id { get; set; }

    public required string Name { get; set; } = string.Empty;
    public required int CategoryId { get; set; }

    #endregion

    #region Navigation Properties

    public virtual Category Category { get; set; } = null!;
    public virtual ICollection<Expense> Expenses { get; set; } = [];
    public virtual ICollection<Tag> Tags { get; set; } = new List<Tag>();

    #endregion
}