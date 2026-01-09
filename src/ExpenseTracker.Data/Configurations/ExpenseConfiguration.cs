using System.Text.Json;
using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Data.Configurations;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        
        builder.HasOne(e => e.SubCategory)
            .WithMany(c => c.Expenses)
            .HasForeignKey(e => e.SubCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Tag)
            .WithMany(t => t.Expenses)
            .HasForeignKey(e => e.TagId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}