using ClosedXML.Excel;
using AlbertQuackmore.Data;
using AlbertQuackmore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlbertQuackmore.Services;

public class ImportResult
{
    public int CategoriesCreated { get; set; }
    public int SubCategoriesCreated { get; set; }
    public int TagsCreated { get; set; }
    public int BudgetsCreated { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public class ImportService(AppDbContext context, ILogger<ImportService> logger)
{
    public async Task<ImportResult> ImportFromExcelAsync(Stream fileStream, int year)
    {
        var result = new ImportResult();

        try
        {
            using var workbook = new XLWorkbook(fileStream);
            var worksheet = workbook.Worksheets.First();

            string? currentCategoryName = null;
            Category? currentCategory = null;

            // Skip header row, start from row 2
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

            for (var row = 2; row <= lastRow; row++)
            {
                var categoryCell = worksheet.Cell(row, 1).GetString().Trim();
                var subCategoryName = worksheet.Cell(row, 2).GetString().Trim();
                var tagsCell = worksheet.Cell(row, 4).GetString().Trim();
                var budgetCell = worksheet.Cell(row, 5);

                // Skip empty rows or total rows
                if (string.IsNullOrWhiteSpace(subCategoryName) ||
                    subCategoryName.StartsWith("TOTALE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // New category?
                if (!string.IsNullOrWhiteSpace(categoryCell))
                {
                    currentCategoryName = categoryCell;
                    currentCategory = await GetOrCreateCategoryAsync(currentCategoryName, result);
                }

                if (currentCategory == null)
                {
                    result.Errors.Add($"Riga {row}: Nessuna categoria trovata per '{subCategoryName}'");
                    continue;
                }

                // Create subcategory
                var subCategory = await GetOrCreateSubCategoryAsync(currentCategory.Id, subCategoryName, result);

                // Create tags
                if (!string.IsNullOrWhiteSpace(tagsCell))
                {
                    var tagNames = tagsCell.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim())
                        .Where(t => !string.IsNullOrWhiteSpace(t));

                    foreach (var tagName in tagNames)
                    {
                        await GetOrCreateTagAsync(subCategory.Id, tagName, result);
                    }
                }

                // Create budgets - read default and monthly overrides
                // Column E = default, Columns F-Q = Jan-Dec overrides
                var defaultBudget = budgetCell.TryGetValue<decimal>(out var defaultAmount) ? defaultAmount : 0m;
                var monthlyBudgets = new decimal[12];

                for (var month = 1; month <= 12; month++)
                {
                    // Columns F-Q are columns 6-17 (month 1=col 6, month 12=col 17)
                    var monthCell = worksheet.Cell(row, 5 + month);
                    if (monthCell.TryGetValue<decimal>(out var monthAmount) && monthAmount > 0)
                    {
                        monthlyBudgets[month - 1] = monthAmount;
                    }
                    else
                    {
                        monthlyBudgets[month - 1] = defaultBudget;
                    }
                }

                // Only create budgets if we have at least one non-zero value
                if (monthlyBudgets.Any(b => b > 0))
                {
                    await CreateBudgetsAsync(subCategory.Id, year, monthlyBudgets, result);
                }
            }

            await context.SaveChangesAsync();
            logger.LogInformation(
                "Import completed: {Categories} categories, {SubCategories} subcategories, {Tags} tags, {Budgets} budgets",
                result.CategoriesCreated, result.SubCategoriesCreated, result.TagsCreated, result.BudgetsCreated);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error importing Excel file");
            result.Errors.Add($"Errore durante l'importazione: {ex.Message}");
        }

        return result;
    }

    private async Task<Category> GetOrCreateCategoryAsync(string name, ImportResult result)
    {
        var existing = await context.Categories.FirstOrDefaultAsync(c => c.Name == name);
        if (existing != null)
        {
            result.Warnings.Add($"Categoria '{name}' già esistente");
            return existing;
        }

        var category = new Category { Name = name };
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        result.CategoriesCreated++;
        logger.LogDebug("Created category: {Name}", name);
        return category;
    }

    private async Task<SubCategory> GetOrCreateSubCategoryAsync(int categoryId, string name, ImportResult result)
    {
        var existing = await context.SubCategories
            .FirstOrDefaultAsync(sc => sc.CategoryId == categoryId && sc.Name == name);

        if (existing != null)
        {
            result.Warnings.Add($"Sottocategoria '{name}' già esistente");
            return existing;
        }

        var subCategory = new SubCategory { Name = name, CategoryId = categoryId };
        context.SubCategories.Add(subCategory);
        await context.SaveChangesAsync();
        result.SubCategoriesCreated++;
        logger.LogDebug("Created subcategory: {Name}", name);
        return subCategory;
    }

    private async Task GetOrCreateTagAsync(int subCategoryId, string name, ImportResult result)
    {
        var existing = await context.Tags
            .FirstOrDefaultAsync(t => t.SubCategoryId == subCategoryId && t.Name == name);

        if (existing != null)
        {
            return;
        }

        var tag = new Tag { Name = name, SubCategoryId = subCategoryId };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        result.TagsCreated++;
        logger.LogDebug("Created tag: {Name}", name);
    }

    private async Task CreateBudgetsAsync(int subCategoryId, int year, decimal[] monthlyAmounts, ImportResult result)
    {
        for (var month = 1; month <= 12; month++)
        {
            var amount = monthlyAmounts[month - 1];
            if (amount <= 0) continue;

            var existing = await context.Budgets
                .FirstOrDefaultAsync(b => b.SubCategoryId == subCategoryId && b.Year == year && b.Month == month);

            if (existing != null)
            {
                existing.Amount = amount;
            }
            else
            {
                var budget = new Budget
                {
                    SubCategoryId = subCategoryId,
                    Year = year,
                    Month = month,
                    Amount = amount
                };
                context.Budgets.Add(budget);
                result.BudgetsCreated++;
            }
        }

        logger.LogDebug("Created/updated budgets for subcategory {SubCategoryId}, year {Year}",
            subCategoryId, year);
    }
}
