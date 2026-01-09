using ExpenseTracker.Data;
using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services;

public class ExpenseService(AppDbContext context)
{
    public async Task CreateExpenseAsync(int subCategoryId, decimal amount, string description, string? notes, string performedBy, int? tagId)
    {
        var expense = new Expense
        {
            SubCategoryId = subCategoryId,
            Amount = amount,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            TagId = tagId,
            Notes = notes,
            PerformedBy = performedBy
        };
        context.Expenses.Add(expense);
        await context.SaveChangesAsync();
    }

    public async Task<List<Expense>> GetExpensesByDateRangeAsync(DateTime from, DateTime to) =>
        await context.Expenses
            .Include(e => e.SubCategory)
            .ThenInclude(sc => sc.Category)
            .Where(e => e.CreatedAt >= from && e.CreatedAt <= to)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
}