namespace AlbertQuackmore.TelegramBot;

public class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = string.Empty;
    public string[] AllowedUsername { get; set; } = [];
}
