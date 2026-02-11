using SQLite;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.Payments;
using Telegram.Bot.Types.ReplyMarkups;

namespace SneakerBot
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string TOKEN = Environment.GetEnvironmentVariable("TG_BOT_SNEAKER");

            Console.WriteLine("Hello, World!");

            var bot = new Host(TOKEN);
            bot.DBCreate(new DatabaseService());
            bot.Start();
            Console.ReadLine();
        }
    }

    class Host
    {
        DatabaseService db;
        TelegramBotClient bot;
        public Dictionary<long, UserState> _states = new Dictionary<long, UserState>();

        public void DBCreate(DatabaseService db)
        {
            this.db = db;
            Console.WriteLine("База подключена!");

            var products = db.GetProducts();
            foreach (var p in products)
            {
                Console.WriteLine($"ID: {p.Id} | Товар: {p.Name} | Цена: {p.Price}");
            }
        }

        public Host(string token)
        {
            bot = new TelegramBotClient(token);
        }

        public void Start()
        {
            bot.StartReceiving(UpdateHandler, ErrorHandler);
            Console.WriteLine("bot has been started");
        }

        private async Task ErrorHandler(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken token)
        {
            Console.WriteLine(exception.Message);
        }

        private async Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken token)
        {
            long chatId = 0;
            Telegram.Bot.Types.User? telegramUser = null;

            if (update.Message != null)
            {
                chatId = update.Message.Chat.Id;
                telegramUser = update.Message.From;
            }
            else if (update.CallbackQuery != null)
            {
                chatId = update.CallbackQuery.Message.Chat.Id;
                telegramUser = update.CallbackQuery.From;
            }
            else if (update.PreCheckoutQuery != null) 
            {
                chatId = update.PreCheckoutQuery.From.Id;
                telegramUser = update.PreCheckoutQuery.From;
            }
            if (chatId == 0) return;

            if (telegramUser != null)
            {
                if (!db.UserExists(chatId))
                {
                    var newUser = new User
                    {
                        ChatId = chatId,
                        Username = telegramUser.Username ?? "Без ника",
                        FirstName = telegramUser.FirstName ?? "Аноним",
                        RegistrationDate = DateTime.Now
                    };
                    db.AddUser(newUser);
                    Console.WriteLine($"🎉 Новый пользователь: {newUser.FirstName} ({chatId})");
                }
            }

            if (!_states.TryGetValue(chatId, out var state))
            {
                state = new UserState();
                _states[chatId] = state;
            }

            switch (update.Type)
            {
                case UpdateType.Message:
                    if (update.Message.SuccessfulPayment != null)
                    {
                        string payload = update.Message.SuccessfulPayment.InvoicePayload;
                        if (payload.StartsWith("purchase_"))
                        {
                            string[] parts = payload.Split('_');
                            if (parts.Length == 2 && int.TryParse(parts[1], out int purchasedProductId))
                            {
                                await bot.SendMessage(chatId, $"✅ Спасибо за покупку! Вы купили товар с ID: {purchasedProductId}.");
                                var product = db.GetProductById(purchasedProductId);
                                if (product != null)
                                {
                                    await bot.SendPhoto(1369750317, photo: InputFile.FromFileId(product.PhotoId), caption: $"Пользователь @{update.Message.From?.Username ?? "N/A"} оплатил продукт {product.Name}: {product.Id}");
                                }
                            }
                            else
                            {
                                await bot.SendMessage(chatId, "Произошла ошибка при обработке ID товара.");
                            }
                        }
                        else
                        {
                            await bot.SendMessage(chatId, "Неизвестный тип оплаты.");
                        }
                        return; 
                    }

                    if (state.isAddingProduct)
                    {
                        await DialogHandler(update.Message, chatId, state);
                    }
                    else 
                    {
                        if (update.Message.Text != null) 
                        {
                            await TextMessageHandler(update.Message, chatId, state);
                        }
                    }
                    break;

                case UpdateType.CallbackQuery:
                    await CallBackQueryHandler(update.CallbackQuery, chatId, state);
                    break;

                case UpdateType.PreCheckoutQuery:
                    await bot.AnswerPreCheckoutQuery(update.PreCheckoutQuery.Id);
                    break;
                default:
                    Console.WriteLine($"Неизвестный тип обновления: {update.Type}");
                    break;
            }
        }

        async Task ShowCatalog(Message message, long chatId, UserState state)
        {
            var products = db.GetProducts();

            if(products.Count == 0)
            {
                await bot.SendMessage(chatId, "Каталог на данный момент пуст. Загляни позже!");
            }

            foreach (var p in products)
            {
                string caption = $"👟 *{p.Name}*\n\n💰 Цена: *{p.Price} руб.*";

                var buyButton = InlineKeyboardButton.WithCallbackData("Купить за Stars ✨", $"buy:{p.Id}");
                var keyboard = new InlineKeyboardMarkup(buyButton);

                await bot.SendPhoto(
                    chatId,
                    photo: InputFile.FromFileId(p.PhotoId),
                    caption: caption,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard 
                );

                await Task.Delay(200);
            }
        }

        async Task CallBackQueryHandler(CallbackQuery callbackQuery, long chatId, UserState state)
        {
            var text = callbackQuery.Data;

            if (text == null) return;

            if(text == "Добавить👟")
            {
                await bot.SendMessage(chatId, "Введите название товара");
                state.isAddingProduct = true;
                state.Step = 1;
            }
            else if(text == "Удалить👟")
            {
                string str = "Введите ID товара";
                var products = db.GetProducts();

                if (products.Count == 0)
                {
                    await bot.SendMessage(chatId, "Каталог пуст.");
                    return;
                }

                foreach (var p in products)
                {
                    str += $"\n{p.Name}: {p.Id}";
                }
                await bot.SendMessage(chatId, str);
                state.isAddingProduct = true;
                state.Step = 4;
            }
            else if(text == "Статистика🖥")
            {
                var allUsers = db.GetAllUsers();
                int totalUsers = allUsers.Count;

                int todayNewUsers = allUsers.Count(u => u.RegistrationDate.Date == DateTime.Today);

                string userStats = $"\n\n👥 *Пользователи*\n" +
                                   $"Всего в базе: *{totalUsers}*\n" +
                                   $"Новых за сегодня: *{todayNewUsers}*";
                await bot.SendMessage(chatId, userStats, parseMode: ParseMode.Markdown);
            }

            else if(text.StartsWith("buy:"))
            {
                if (!int.TryParse(text.Substring(4), out int productId)) return;

                var productToBuy = db.GetProductById(productId);

                if (productToBuy != null)
                {
                    await bot.SendInvoice(
                        chatId: chatId,
                        title: $"Покупка: {productToBuy.Name}",
                        description: $"Кроссовки {productToBuy.Name} по цене {productToBuy.Price} Stars",
                        payload: $"purchase_{productId}", 
                        providerToken: "XTR", 
                        currency: "XTR", 
                        prices: new[] { new LabeledPrice(productToBuy.Name, productToBuy.Price) }, 
                        maxTipAmount: 0, 
                        needShippingAddress: false
                    );
                }
                else
                {
                    await bot.SendMessage(chatId, "Извините, товар не найден.");
                }
            }
        }

        async Task TextMessageHandler(Message message, long chatId, UserState state)
        {
            var text = message.Text;

            if(text == "/start")
            {
                await bot.SendMessage(chatId, "hello!\n/catalog");
            }
            else if(text == "123")
            {
                await bot.SendMessage(chatId, "Права админа получены\n/admin");
                state.isAdmin = true;
            }
            else if (text == "/admin" && state.isAdmin)
            {
                await bot.SendMessage(chatId, $"Добро пожаловть в адмни-панель, {message.From?.FirstName}",
                    replyMarkup: new InlineKeyboardButton[][]
                    {
                        [("Добавить👟")],
                        [("Удалить👟")],
                        [("Статистика🖥")]
                    });
            }
            else if (text == "/add" && state.isAdmin)
            {
                await bot.SendMessage(chatId, "Введите название товара");
                state.isAddingProduct = true;
                state.Step = 1;
            }
            else if (text == "/remove" && state.isAdmin)
            {
                string str = "Введите ID товара";
                var products = db.GetProducts();
                foreach (var p in products)
                {
                    str += $"\n{p.Name}: {p.Id}";
                }
                await bot.SendMessage(chatId, str);
                state.isAddingProduct = true;
                state.Step = 4;
            }
            else if (text == "/catalog")
            {
                await ShowCatalog(message, chatId, state);
            }
        }

        async Task DialogHandler(Message message, long chatId, UserState state)
        {
            var text = message.Text;

            if(text == null && state.Step != 3)
            {
                await bot.SendMessage(chatId, "Неверный Формат!");
                return;
            }

            if(state.Step == 1)
            {
                state.ProductName = text;
                state.Step++;
                await bot.SendMessage(chatId, "Введите цену (только чилсо)");
            }
            else if(state.Step == 2)
            {
                if (!int.TryParse(text, out int price))
                {
                    await bot.SendMessage(chatId, "Неверный формат (введите только число!)");
                    return;
                }

                state.ProductPrice = price;
                state.Step++;
                await bot.SendMessage(chatId, "Отправьте фото продукта");
            }
            else if(state.Step == 3)
            {
                if (message.Photo == null)
                {
                    await bot.SendMessage(chatId, "Неверный формат (пришлите фото!)");
                    return;
                }

                string fileId = message.Photo.Last().FileId;

                Product newProduct = new Product
                {
                    Name = state.ProductName,
                    Price = state.ProductPrice,
                    PhotoId = fileId
                };

                db.AddProduct(newProduct);
                await bot.SendMessage(chatId, $"✅ Товар '{newProduct.Name}' сохранен в базу!");
                state.isAddingProduct = false;
                state.Step = 0;
            }
            else if(state.Step == 4)
            {
                if(!int.TryParse(text, out int id))
                {
                    await bot.SendMessage(chatId, "Неверный формат (введите число!)");
                    return;
                }

                bool isDeleted = db.DeleteProduct(id);
                if (isDeleted)
                {
                    await bot.SendMessage(chatId, $"Товар {id} удалён из каталога!\n/add\n/catalog");
                    state.isAddingProduct = false;
                    state.Step = 0;
                }
                else
                {
                    await bot.SendMessage(chatId, $"Товар {id} не найден");
                    state.isAddingProduct = false;
                    state.Step = 0;
                }
            }
        }
    }

    public class Product
    {
        [PrimaryKey, AutoIncrement] 
        public int Id { get; set; }

        public string Name { get; set; }
        public int Price { get; set; }
        public string PhotoId { get; set; } 

        public Product() { }

        public Product(string name, int price, string photoId)
        {
            Name = name;
            Price = price;
            PhotoId = photoId;
        }
    }

    public class DatabaseService
    {
        private readonly SQLiteConnection _connection;

        public DatabaseService(string dbName = "shop.db")
        {
            string dbPath = Path.Combine(Environment.CurrentDirectory, dbName);
            _connection = new SQLiteConnection(dbPath);

            _connection.CreateTable<Product>();

            _connection.CreateTable<User>();
        }

        public bool UserExists(long chatId)
        {
            var user = _connection.Table<User>().FirstOrDefault(u => u.ChatId == chatId);
            return user != null;
        }

        public void AddProduct(Product product)
        {
            _connection.Insert(product);
        }
        public void AddUser(User user)
        {
            _connection.Insert(user);
        }

        public List<Product> GetProducts()
        {
            return _connection.Table<Product>().ToList();
        }
        public Product GetProductById(int id)
        {
            return _connection.Table<Product>().FirstOrDefault(p => p.Id == id);
        }
        public List<User> GetAllUsers()
        {
            return _connection.Table<User>().ToList();
        }

        public bool DeleteProduct(int id)
        {
            int deleted = _connection.Delete<Product>(id);

            return deleted > 0;
        }
    }

    class UserState
    {
        public bool isAdmin {  get; set; }
        public bool isAddingProduct { get; set; }
        public int Step { get; set; }

        public string? ProductName { get; set; }
        public int ProductPrice { get; set; }
        public string? ProductPhotoId { get; set; }
    }

    public class User
    {
        [PrimaryKey] 
        public long ChatId { get; set; }

        public string Username { get; set; } 
        public string FirstName { get; set; } 
        public DateTime RegistrationDate { get; set; } 
    }
}
