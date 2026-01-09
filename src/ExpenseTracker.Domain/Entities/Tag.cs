namespace ExpenseTracker.Domain.Entities;

public class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int SubCategoryId { get; set; }

    public virtual SubCategory SubCategory { get; set; } = null!;
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}

