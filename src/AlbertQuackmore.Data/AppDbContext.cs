using System.ComponentModel;
using AlbertQuackmore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlbertQuackmore.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<SubCategory> SubCategories => Set<SubCategory>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}