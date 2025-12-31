using ExpenseTracker.Data;
using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services;

public class CategoryService(AppDbContext context)
{
    public async Task<List<Category>> GetAllCategoriesAsync() =>
        await context.Categories
            .OrderBy(c => c.Name)
            .ToListAsync();

    public async Task<Category?> GetCategoryByIdAsync(int id) =>
        await context.Categories
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Id == id);

    public async Task<List<SubCategory>> GetSubCategoriesByCategoryIdAsync(int categoryId) =>
        await context.SubCategories
            .Where(sc => sc.CategoryId == categoryId)
            .OrderBy(sc => sc.Name)
            .ToListAsync();

    public async Task<SubCategory?> GetSubCategoryByIdAsync(int id) =>
        await context.SubCategories
            .FirstOrDefaultAsync(sc => sc.Id == id);
}