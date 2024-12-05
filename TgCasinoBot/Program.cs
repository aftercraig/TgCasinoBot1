using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text.Json;
using System;

public class Giveaway
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; }
    public DateTime? EndTime { get; set; }
    public List<long> Participants { get; set; } = new List<long>();
    public bool IsCompleted { get; set; } = false;
}

public static class GiveawayStorage
{
    private static readonly string FilePath = "giveaways.json";

    public static List<Giveaway> LoadGiveaways()
    {
        if (!System.IO.File.Exists(FilePath))
        {
            System.IO.File.WriteAllText(FilePath, "[]");
        }
        var json = System.IO.File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<List<Giveaway>>(json) ?? new List<Giveaway>();
    }

    public static void SaveGiveaways(List<Giveaway> giveaways)
    {
        var json = JsonSerializer.Serialize(giveaways, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(FilePath, json);
    }
}

public class GiveawayBot
{
    private static readonly string Token;
    private static readonly TelegramBotClient Bot;
    private static List<Giveaway> Giveaways;
    private static readonly Dictionary<long, string> UserGiveawaySetup = new();

    static GiveawayBot()
    {
        try
        {
            Token = "7444413593:AAFHLlvqgpqVcupxGKkZOzbyhLVVgM1vyGA";
            Bot = new TelegramBotClient(Token);
            Giveaways = GiveawayStorage.LoadGiveaways();
            CheckGiveawayEndTimes();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка инициализации: {ex.Message}");
            throw;
        }
    }

    private static async void CheckGiveawayEndTimes()
    {
        while (true)
        {
            foreach (var giveaway in Giveaways.Where(g => g.EndTime.HasValue && !g.IsCompleted))
            {
                if (giveaway.EndTime <= DateTime.Now)
                {
                    await FinishGiveawayAsync(giveaway);
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(1)); // проверка на 1 минуту
        }
    }

    private static async Task FinishGiveawayAsync(Giveaway giveaway)
    {
        if (giveaway.Participants.Any())
        {
            var random = new Random();
            var winnerId = giveaway.Participants[random.Next(giveaway.Participants.Count)];

            var winner = await Bot.GetChatMemberAsync(giveaway.Participants.First(), winnerId);
            string winnerName = winner.User.Username ?? "Неизвестный пользователь";

            await Bot.SendTextMessageAsync(winnerId, $"Розыгрыш '{giveaway.Title}' завершён! Победитель: @{winnerName}");

            giveaway.IsCompleted = true;
            GiveawayStorage.SaveGiveaways(Giveaways);
        }
    }

    public static async Task Main()
    {
        var cts = new CancellationTokenSource();
        Bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync, cancellationToken: cts.Token);
        Console.WriteLine("Бот запущен.");
        Console.ReadLine();
        cts.Cancel();
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is { Text: { } text } message)
        {
            var chatId = message.Chat.Id;

            if (UserGiveawaySetup.TryGetValue(chatId, out string giveawayIdString) && Guid.TryParse(giveawayIdString, out Guid giveawayId))
            {
                var giveaway = Giveaways.FirstOrDefault(g => g.Id == giveawayId);

                if (string.IsNullOrEmpty(giveaway.Title) || giveaway.Title.StartsWith("Розыгрыш №"))
                {
                    giveaway.Title = text;
                    GiveawayStorage.SaveGiveaways(Giveaways);
                    await bot.SendTextMessageAsync(chatId, "Введите дату и время окончания розыгрыша (в формате ЧЧ:ММ, ДД.ММ.ГГГГ):");
                }
                else if (!giveaway.EndTime.HasValue)
                {
                    if (DateTime.TryParseExact(text, "HH:mm, dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime endTime))
                    {
                        if (endTime < DateTime.Now)
                        {
                            await bot.SendTextMessageAsync(chatId, "Дата окончания не может быть раньше текущего времени.");
                        }
                        else
                        {
                            giveaway.EndTime = endTime;
                            GiveawayStorage.SaveGiveaways(Giveaways);
                            await bot.SendTextMessageAsync(chatId, $"Розыгрыш завершится в {endTime:dd.MM.yyyy HH:mm}.");
                            UserGiveawaySetup.Remove(chatId);
                        }
                    }
                    else
                    {
                        await bot.SendTextMessageAsync(chatId, "Введите дату и время в правильном формате (ЧЧ:ММ, ДД.ММ.ГГГГ).");
                    }
                }
            }
            else if (text == "/start")
            {
                var buttons = new InlineKeyboardMarkup(new[] {
                    new[] { InlineKeyboardButton.WithCallbackData("Создать розыгрыш", "create_giveaway") },
                    new[] { InlineKeyboardButton.WithCallbackData("Список розыгрышей", "list_giveaways") }
                });

                await bot.SendTextMessageAsync(chatId, "Добро пожаловать! Выберите действие:", replyMarkup: buttons);
            }
        }
        else if (update.CallbackQuery is { } callbackQuery)
        {
            await HandleCallbackQueryAsync(bot, callbackQuery, cancellationToken);
        }
    }

    private static async Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message.Chat.Id;
        var data = callbackQuery.Data;

        if (data == "create_giveaway")
        {
            var newGiveaway = new Giveaway
            {
                Title = "Розыгрыш №" + (Giveaways.Count + 1),
                EndTime = null
            };

            Giveaways.Add(newGiveaway);
            GiveawayStorage.SaveGiveaways(Giveaways);

            UserGiveawaySetup[chatId] = newGiveaway.Id.ToString();

            await bot.SendTextMessageAsync(chatId, "Введите название розыгрыша:");
        }
        else if (data == "list_giveaways")
        {
            if (!Giveaways.Any())
            {
                await bot.SendTextMessageAsync(chatId, "Список розыгрышей пуст.");
                return;
            }

            foreach (var giveaway in Giveaways)
            {
                var buttons = new InlineKeyboardMarkup(new[] {
                    new[] { InlineKeyboardButton.WithCallbackData("Участвовать", $"participate_{giveaway.Id}") },
                    new[] { InlineKeyboardButton.WithCallbackData("Удалить", $"delete_{giveaway.Id}") }
                });

                var message = $"Розыгрыш: {giveaway.Title}\n" +
                              $"Участников: {giveaway.Participants.Count}\n" +
                              $"Статус: {(giveaway.IsCompleted ? "Завершен" : "Активен")}\n" +
                              $"Окончание: {(giveaway.EndTime.HasValue ? giveaway.EndTime.Value.ToString("dd.MM.yyyy HH:mm") : "Не установлено")}";

                await bot.SendTextMessageAsync(chatId, message, replyMarkup: buttons);
            }
        }
        else if (data.StartsWith("participate_"))
        {
            var giveawayIdString = data.Substring("participate_".Length);

            if (Guid.TryParse(giveawayIdString, out Guid giveawayId))
            {
                var giveaway = Giveaways.FirstOrDefault(g => g.Id == giveawayId);

                if (giveaway != null)
                {
                    if (!giveaway.Participants.Contains(chatId))
                    {
                        giveaway.Participants.Add(chatId);
                        GiveawayStorage.SaveGiveaways(Giveaways);

                        await bot.SendTextMessageAsync(chatId, "Вы участвуете в розыгрыше");
                    }
                    else
                    {
                        await bot.SendTextMessageAsync(chatId, "Вы уже участвуете в этом розыгрыше");
                    }
                }
            }
        }
        else if (data.StartsWith("delete_"))
        {
            var giveawayIdString = data.Substring("delete_".Length);

            if (Guid.TryParse(giveawayIdString, out Guid giveawayId))
            {
                var giveaway = Giveaways.FirstOrDefault(g => g.Id == giveawayId);

                if (giveaway != null)
                {
                    Giveaways.Remove(giveaway);
                    GiveawayStorage.SaveGiveaways(Giveaways);
                    await bot.SendTextMessageAsync(chatId, "Розыгрыш был удален.");
                }
            }
        }
    }

    private static Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Ошибка: {exception.Message}");
        return Task.CompletedTask;
    }
}
