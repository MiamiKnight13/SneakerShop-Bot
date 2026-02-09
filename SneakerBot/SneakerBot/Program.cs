using SQLite;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
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
            if (update.Message == null && update.CallbackQuery == null) return;

            var message = update.Message;
            var chatId = message?.Chat.Id ?? update.CallbackQuery.Message.Chat.Id;
            var text = message?.Text;

            var fromUser = update.Message?.From ?? update.CallbackQuery?.From;

            if (fromUser != null)
            {
                if (!db.UserExists(chatId))
                {
                    var newUser = new User
                    {
                        ChatId = chatId,
                        Username = fromUser.Username ?? "Без ника",
                        FirstName = fromUser.FirstName ?? "Аноним",
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

            if (state.isAddingProduct && message != null)
            {
                await DialogHandler(message, chatId, state);
                return;
            }
            if (message?.Text != null)
            {
                await TextMessageHandler(message, chatId, state);
                return;
            }
            if(update.CallbackQuery != null)
            {
                await CallBackQueryHandler(update.CallbackQuery, chatId, state);
            }
        }

        async Task ShowCatalog(Message message, long chatId, UserState state)
        {
            var products = db.GetProducts();

            if(products.Count == 0)
            {
                await bot.SendMessage(chatId, "Каталог на данный момент пуст. Загляни позже!");
            }

            foreach(var p in products)
            {
                await bot.SendPhoto(chatId, InputFile.FromFileId(p.PhotoId), caption: $"👟*{p.Name}*\nЦена: *{p.Price}*", parseMode: ParseMode.Markdown);
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
                await bot.SendMessage(chatId, "!");
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
