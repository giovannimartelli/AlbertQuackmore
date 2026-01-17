using AlbertQuackmore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlbertQuackmore.Data.Configurations;

public class SubCategoryConfiguration : IEntityTypeConfiguration<SubCategory>
{
    public void Configure(EntityTypeBuilder<SubCategory> builder)
    {
        builder.ToTable("SubCategories");

        builder.HasKey(sc => sc.Id);
        builder.HasIndex(c => c.Name).IsUnique();

        builder.Property(sc => sc.Id).ValueGeneratedOnAdd();

        builder.HasOne(sc => sc.Category)
            .WithMany(c => c.Children)
            .HasForeignKey(sc => sc.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
