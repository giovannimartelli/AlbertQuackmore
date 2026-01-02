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

    public async Task CreateNewCategoryAsync(string name)
    {
        var cat = await context.Categories.SingleOrDefaultAsync(c => c.Name == name);
        if (cat is null)
            context.Categories.Add(new Category
            {
                Name = name
            });
        await context.SaveChangesAsync();
    }
    
    public async Task CreateNewSubCategoryAsync(string name, int categoryId)
    {
        var cat = await context.SubCategories.SingleOrDefaultAsync(c => c.Name == name && c.CategoryId == categoryId);
        if (cat is null)
            context.SubCategories.Add(new SubCategory
            {
                Name = name,
                CategoryId = categoryId
            });
        await context.SaveChangesAsync();
    }
}