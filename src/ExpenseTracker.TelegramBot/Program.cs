using ExpenseTracker.Data;
using ExpenseTracker.Services;
using ExpenseTracker.TelegramBot;
using ExpenseTracker.TelegramBot.TelegramBot;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection(TelegramOptions.SectionName));

builder.Services.AddSingleton<ITelegramBotClient>(_ =>
{
    var config = builder.Configuration.GetSection(TelegramOptions.SectionName).Get<TelegramOptions>();
    return new TelegramBotClient(config?.BotToken ?? throw new InvalidOperationException("Bot token not configured"));
});

builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<ExpenseService>();

builder.Services.AddHostedService<BotService>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying database migrations");
        throw;
    }
}

await host.RunAsync();
