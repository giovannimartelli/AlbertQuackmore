using Telegram.Bot.Types.ReplyMarkups;

namespace ExpenseTracker.TelegramBot.TelegramBot.Utils;

public class Utils
{
    public static string MainMenuStep => "MainMenu";
    public static string CallbackMainMenu => "main";
    public static string CallbackBack => "back";
    public static string CallbackSeparator => "$_$";

    public static InlineKeyboardButton ButtonWithCallbackdata(string text, string callbackName, object callbackData) =>
        InlineKeyboardButton.WithCallbackData(text, $"{callbackName}{CallbackSeparator}{callbackData}");

    public static InlineKeyboardButton MainMenu => InlineKeyboardButton.WithCallbackData("🏠 Main menu", CallbackMainMenu);
    public static InlineKeyboardButton Back => InlineKeyboardButton.WithCallbackData("◀️ Back", CallbackBack);
}