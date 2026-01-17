namespace AlbertQuackmore.Domain.Entities;

public class Category
{
    #region Properties

    public int Id { get; set; }

    public required string Name { get; set; } = string.Empty;

    #endregion


    #region Navigation properties

    public virtual ICollection<SubCategory> Children { get; set; } = new List<SubCategory>();

    #endregion
}