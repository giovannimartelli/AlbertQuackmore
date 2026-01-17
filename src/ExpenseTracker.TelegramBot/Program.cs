using System.Reflection;
using ExpenseTracker.Data;
using ExpenseTracker.Services;
using ExpenseTracker.TelegramBot;
using ExpenseTracker.TelegramBot.TelegramBot;
using ExpenseTracker.TelegramBot.TelegramBot.Flows;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection(TelegramOptions.SectionName));

builder.Services.Configure<WebAppOptions>(
    builder.Configuration.GetSection(WebAppOptions.SectionName));

builder.Services.Configure<FlowOptions>(
    builder.Configuration.GetSection(FlowOptions.SectionName));

builder.Services.AddSingleton<ITelegramBotClient>(_ =>
{
    var config = builder.Configuration.GetSection(TelegramOptions.SectionName).Get<TelegramOptions>();
    return new TelegramBotClient(config?.BotToken ?? throw new InvalidOperationException("Bot token not configured"));
});

builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<ImportService>();

// Auto-discover and register enabled FlowHandler implementations as Singleton
// FlowHandlers are stateless (state is passed as parameter) and use IServiceScopeFactory for scoped dependencies
var flowOptions = builder.Configuration.GetSection(FlowOptions.SectionName).Get<FlowOptions>();
var enabledFlows = flowOptions?.EnabledFlows ?? [];

if (enabledFlows.Length == 0)
    throw new ArgumentException("Missing Flows configuration. Configure Flows.EnabledFlows");

var flowHandlerTypes = Assembly.GetExecutingAssembly()
    .GetTypes()
    .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(FlowHandler)))
    .Where(t =>
    {
        // Get the Flow attribute
        var flowAttribute = t.GetCustomAttribute<FlowAttribute>();
        if (flowAttribute == null)
        {
            throw new InvalidOperationException(
                $"FlowHandler '{t.Name}' must be decorated with [Flow(\"FlowName\")] attribute");
        }

        return enabledFlows.Contains(flowAttribute.Name, StringComparer.OrdinalIgnoreCase);
    });

foreach (var handlerType in flowHandlerTypes)
{
    builder.Services.AddSingleton(typeof(FlowHandler), handlerType);
}

// Register FlowController as Singleton (stateless, menu keyboard is built once)
builder.Services.AddSingleton<FlowController>();
// Lazy<FlowController> to break circular dependency (FlowHandler -> FlowController -> FlowHandler)
builder.Services.AddSingleton(sp => new Lazy<FlowController>(sp.GetRequiredService<FlowController>));

// Register the new BotService that uses FlowController
builder.Services.AddHostedService<BotService>();

// builder.Services.AddHostedService<BotService>();

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