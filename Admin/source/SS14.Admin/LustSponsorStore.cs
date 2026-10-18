using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SS14.Admin;

// Lust edit - магазин, заявки и подписки сохраняются одной атомарной транзакцией.
public sealed class LustSponsorStore(IConfiguration config, IWebHostEnvironment env, GribleAdminAccess access)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static readonly string[] Currencies = ["RUB", "PLN", "EUR"];
    public static readonly string[] Periods = ["month", "quarter", "year"];
    private const int CurrentCatalogRevision = 2;
    private string DirectoryPath => Path.GetFullPath(config["Grible:RewardsDirectory"] ?? "private/rewards", env.ContentRootPath);
    public static string Symbol(string currency) => currency switch { "PLN" => "zł", "EUR" => "€", _ => "₽" };
    public static string CurrencyName(string currency) => currency switch { "PLN" => "Злотые", "EUR" => "Евро", _ => "Рубли" };
    public static string PeriodName(string period) => period switch { "quarter" => "3 месяца", "year" => "Год", _ => "Месяц" };
    public static string Amount(decimal amount) => amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    // Lust edit: стоимость и срок рассчитываются на сервере; форма не задаёт сумму или скидку.
    public static SubscriptionQuote Quote(Tier source, string currency, string period)
    {
        if (!Periods.Contains(period)) throw new ArgumentException("Выбери срок подписки: месяц, три месяца или год.");
        if (!Currencies.Contains(currency) || !source.Prices.TryGetValue(currency, out var price) || price <= 0)
            throw new ArgumentException("Для этой валюты не настроена цена.");
        var months = period switch { "quarter" => 3, "year" => 12, _ => 1 };
        var discount = period switch { "quarter" => 20, "year" => 30, _ => 0 };
        var tier = CloneTier(source);
        tier.Days = period == "year" && source.Days == 30 ? 365 : source.Days * months;
        var total = decimal.Round(price * months * (100 - discount) / 100, 2, MidpointRounding.AwayFromZero);
        return new(tier, period, months, discount, total, decimal.Round(total / months, 2, MidpointRounding.AwayFromZero));
    }
    public static string Count(int count, string one, string few, string many)
    {
        var lastTwo = Math.Abs(count) % 100;
        var word = lastTwo is >= 11 and <= 14 ? many : (lastTwo % 10) switch { 1 => one, >= 2 and <= 4 => few, _ => many };
        return $"{count} {word}";
    }

    public async Task<PublicShop> ReadPublicAsync()
    {
        var doc = await ReadAsync();
        return new(doc.Tiers.OrderBy(t => t.Rank).ToList(), (await CatalogAsync()).Items, doc.PaymentInstructions);
    }
    public async Task<Document> ReadAdminAsync(Guid actor)
    {
        await AuthorizeAsync(actor);
        return await ReadAsync();
    }

    public async Task<(Guid Id, string Key)> CreateOrderAsync(Guid userId, string playerName, string tierKey, string currency, string period = "month")
    {
        if (userId == Guid.Empty || playerName.Length > 60 || !Currencies.Contains(currency)) throw new ArgumentException("Проверь идентификатор аккаунта, имя аккаунта и валюту.");
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return await MutateAsync(doc =>
        {
            var tier = doc.Tiers.SingleOrDefault(t => t.Key == tierKey) ?? throw new ArgumentException("Уровень не найден.");
            var quote = Quote(tier, currency, period);
            if (doc.Orders.Count(o => o.UserId == userId && o.Status == "pending") >= 5) throw new ArgumentException("У этого аккаунта уже есть пять ожидающих заявок. Дождись проверки одной из них.");
            var order = new Order
            {
                Id = Guid.NewGuid(), UserId = userId, PlayerName = playerName.Trim(), Tier = quote.Tier,
                Currency = currency, Amount = quote.Total, Period = period, Months = quote.Months, Discount = quote.Discount, CreatedAt = DateTimeOffset.UtcNow,
                KeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))
            };
            doc.Orders.Add(order);
            return (order.Id, key);
        });
    }

    public async Task<Order?> FindOrderAsync(Guid id, string key)
    {
        if (key.Length != 64) return null;
        var order = (await ReadAsync()).Orders.SingleOrDefault(o => o.Id == id);
        if (order == null) return null;
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return CryptographicOperations.FixedTimeEquals(supplied, Convert.FromHexString(order.KeyHash)) ? order : null;
    }

    public async Task<bool> ConfirmAsync(Guid actor, Guid orderId)
    {
        await AuthorizeAsync(actor);
        return await MutateAsync(doc =>
        {
            var order = doc.Orders.SingleOrDefault(o => o.Id == orderId) ?? throw new ArgumentException("Заявка не найдена.");
            if (order.Status == "confirmed") return false;
            if (order.Status != "pending") throw new ArgumentException("Эта заявка уже закрыта.");
            AddMembership(doc, order.UserId, order.Tier, actor, order.Id);
            order.Status = "confirmed";
            order.HandledBy = actor;
            order.HandledAt = DateTimeOffset.UtcNow;
            doc.History.Add(new("confirm", actor, order.UserId, order.Tier.Name, DateTimeOffset.UtcNow));
            return true;
        });
    }

    public async Task DeclineAsync(Guid actor, Guid orderId, string message)
    {
        await AuthorizeAsync(actor);
        if (message.Length > 300) throw new ArgumentException("Сообщение должно быть не длиннее 300 символов.");
        await MutateAsync(doc =>
        {
            var order = doc.Orders.SingleOrDefault(o => o.Id == orderId) ?? throw new ArgumentException("Заявка не найдена.");
            if (order.Status != "pending") throw new ArgumentException("Эта заявка уже закрыта.");
            order.Status = "declined"; order.Message = message.Trim(); order.HandledBy = actor; order.HandledAt = DateTimeOffset.UtcNow;
            doc.History.Add(new("decline", actor, order.UserId, order.Tier.Name, DateTimeOffset.UtcNow));
            return true;
        });
    }

    public async Task GrantAsync(Guid actor, Guid userId, string tierKey, string period = "month")
    {
        await AuthorizeAsync(actor);
        if (userId == Guid.Empty) throw new ArgumentException("Нужен идентификатор игрового аккаунта.");
        await MutateAsync(doc =>
        {
            var tier = doc.Tiers.SingleOrDefault(t => t.Key == tierKey) ?? throw new ArgumentException("Уровень не найден.");
            AddMembership(doc, userId, Quote(tier, "RUB", period).Tier, actor, null);
            doc.History.Add(new("grant", actor, userId, tier.Name, DateTimeOffset.UtcNow));
            return true;
        });
    }

    public async Task RevokeAsync(Guid actor, Guid membershipId)
    {
        await AuthorizeAsync(actor);
        await MutateAsync(doc =>
        {
            var member = doc.Memberships.SingleOrDefault(m => m.Id == membershipId) ?? throw new ArgumentException("Подписка не найдена.");
            if (member.RevokedAt != null) return false;
            member.RevokedAt = DateTimeOffset.UtcNow;
            doc.History.Add(new("revoke", actor, member.UserId, member.Tier.Name, DateTimeOffset.UtcNow));
            return true;
        });
    }

    public async Task UpdateTierAsync(Guid actor, Tier tier)
    {
        await AuthorizeAsync(actor);
        var catalog = (await CatalogAsync()).Items;
        if (string.IsNullOrWhiteSpace(tier.Name) || tier.Name.Length > 40 || tier.Days is < 1 or > 365 || tier.ExtraSlots is < 0 or > 20 || tier.FlavorLength is < 512 or > 20000 || !Regex.IsMatch(tier.OocColor, "^#[0-9a-fA-F]{6}$") || Currencies.Any(c => !tier.Prices.TryGetValue(c, out var price) || price <= 0 || price > 100000 || decimal.Round(price, 2) != price))
            throw new ArgumentException("Проверь название, цены, срок (1–365 дней), цвет #RRGGBB и привилегии.");
        if (tier.GhostThemes.Any(id => !catalog.Any(i => i.Kind == "ghost" && i.Restricted && i.PrototypeId == id)) || tier.Pets.Any(id => !catalog.Any(i => i.Kind == "pet" && i.Restricted && i.PrototypeId == id)))
            throw new ArgumentException("Выбери облики и питомцев из каталога сервера.");
        await MutateAsync(doc =>
        {
            var index = doc.Tiers.FindIndex(t => t.Key == tier.Key);
            if (index < 0) throw new ArgumentException("Уровень не найден.");
            tier.Rank = doc.Tiers[index].Rank;
            tier.Name = tier.Name.Trim(); tier.GhostThemes = tier.GhostThemes.Distinct().ToArray(); tier.Pets = tier.Pets.Distinct().ToArray();
            doc.Tiers[index] = CloneTier(tier);
            doc.History.Add(new("edit-tier", actor, null, tier.Name, DateTimeOffset.UtcNow));
            return true;
        });
    }

    public async Task UpdateInstructionsAsync(Guid actor, string text)
    {
        await AuthorizeAsync(actor);
        if (text.Length > 2000) throw new ArgumentException("Инструкция должна быть не длиннее 2000 символов.");
        await MutateAsync(doc => { doc.PaymentInstructions = text.Trim(); doc.History.Add(new("instructions", actor, null, "Способ оплаты", DateTimeOffset.UtcNow)); return true; });
    }

    private static void AddMembership(Document doc, Guid userId, Tier tier, Guid actor, Guid? orderId)
    {
        var now = DateTimeOffset.UtcNow;
        var lastEnd = doc.Memberships.Where(m => m.UserId == userId && m.Tier.Key == tier.Key && m.RevokedAt == null && m.ExpiresAt > now).Select(m => m.ExpiresAt).DefaultIfEmpty(now).Max();
        doc.Memberships.Add(new Membership { Id = Guid.NewGuid(), UserId = userId, Tier = CloneTier(tier), StartsAt = lastEnd, ExpiresAt = lastEnd.AddDays(tier.Days), GrantedBy = actor, OrderId = orderId });
    }
    private static Tier CloneTier(Tier tier) => JsonSerializer.Deserialize<Tier>(JsonSerializer.Serialize(tier, Json), Json)!;
    private async Task AuthorizeAsync(Guid actor) { if (!await access.CanManageAsync(actor)) throw new UnauthorizedAccessException(); }

    private async Task<LustRewardsStore.Catalog> CatalogAsync() => await ReadFileAsync<LustRewardsStore.Catalog>("catalog.json") ?? new();
    private async Task<Document> ReadAsync()
    {
        if (await ReadFileAsync<Document>("sponsors.json") is { } document) { CheckVersion(document); if (document.CatalogRevision >= CurrentCatalogRevision) return document; }
        return await MutateAsync(doc => doc);
    }
    private async Task<T> MutateAsync<T>(Func<Document, T> mutate)
    {
        Directory.CreateDirectory(DirectoryPath);
        await using var gate = await AcquireLockAsync();
        var doc = await ReadFileAsync<Document>("sponsors.json") ?? Defaults(await CatalogAsync());
        CheckVersion(doc);
        // Lust edit: однократное обновление витрины сохраняет заявки, купленные условия и персональные выдачи.
        if (doc.CatalogRevision < CurrentCatalogRevision)
        {
            var oldFile = Path.Combine(DirectoryPath, "sponsors.json");
            var backupFile = Path.Combine(DirectoryPath, "sponsors-before-reference.json");
            if (File.Exists(oldFile) && !File.Exists(backupFile)) File.Copy(oldFile, backupFile);
            doc.Tiers = Defaults(await CatalogAsync()).Tiers;
            doc.CatalogRevision = CurrentCatalogRevision;
            doc.History.Add(new("catalog", Guid.Empty, null, "Новые уровни: Бета, Альфа, Дельта, Гамма, Эпсилон", DateTimeOffset.UtcNow));
        }
        var result = mutate(doc);
        var target = Path.Combine(DirectoryPath, "sponsors.json");
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { await JsonSerializer.SerializeAsync(stream, doc, Json); await stream.FlushAsync(); stream.Flush(true); }
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return result;
    }
    private static void CheckVersion(Document doc) { if (doc.Version != 1) throw new InvalidDataException("Неизвестная версия магазина."); }
    private async Task<FileStream> AcquireLockAsync()
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(Path.Combine(DirectoryPath, "sponsors.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { await Task.Delay(25); }
        }
    }
    private async Task<T?> ReadFileAsync<T>(string name) where T : class
    {
        var path = Path.Combine(DirectoryPath, name);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Хранилище магазина превышает 16 МиБ.");
        return await JsonSerializer.DeserializeAsync<T>(stream, Json) ?? throw new InvalidDataException("Хранилище магазина пустое.");
    }

    private static Document Defaults(LustRewardsStore.Catalog catalog)
    {
        var ghosts = catalog.Items.Where(i => i.Kind == "ghost" && i.Restricted).OrderBy(i => i.PrototypeId).Select(i => i.PrototypeId).ToArray();
        // Базовые цветные облики находятся в начале; наборы следующих уровней включают предыдущие.
        var firstGhosts = new[] { "Purple", "Pink", "Yellow", "Gold", "Silver", "White", "Red", "Cat" }.Where(ghosts.Contains).ToArray();
        ghosts = firstGhosts.Concat(ghosts.Except(firstGhosts)).ToArray();
        var pets = catalog.Items.Where(i => i.Kind == "pet" && i.Restricted).OrderBy(i => i.PrototypeId).Select(i => i.PrototypeId).ToArray();
        var names = new[] { "Бета", "Альфа", "Дельта", "Гамма", "Эпсилон" };
        var keys = new[] { "sponsor", "sponsor-plus", "patron", "benefactor", "epsilon" };
        var colors = new[] { "#BD92F6", "#F298D9", "#F6D76C", "#88BFFF", "#FFA1D5" };
        var ghostCounts = new[] { 1, 8, 18, 32, ghosts.Length };
        var petCounts = new[] { 3, 8, 12, 18, pets.Length };
        var pricesRub = new[] { 200m, 350m, 650m, 1000m, 2000m };
        var pricesPln = new[] { 10m, 18m, 33m, 50m, 100m };
        var pricesEur = new[] { 3m, 5m, 9m, 14m, 28m };
        var slots = new[] { 10, 12, 14, 16, 20 };
        var flavor = new[] { 4000, 6000, 8000, 12000, 16000 };
        return new Document
        {
            CatalogRevision = CurrentCatalogRevision,
            Tiers = Enumerable.Range(0, 5).Select(i => new Tier { Key = keys[i], Rank = i + 1, Name = names[i], OocColor = colors[i], ExtraSlots = slots[i], FlavorLength = flavor[i], GhostThemes = ghosts.Take(ghostCounts[i]).ToArray(), Pets = pets.Take(petCounts[i]).ToArray(), Prices = new() { ["RUB"] = pricesRub[i], ["PLN"] = pricesPln[i], ["EUR"] = pricesEur[i] } }).ToList()
        };
    }

    public sealed class Document
    {
        public int Version { get; set; } = 1;
        public int CatalogRevision { get; set; }
        public string PaymentInstructions { get; set; } = "Создай заявку и сохрани ссылку на неё. Оплата подтверждается администратором вручную. Владелец сервера должен добавить здесь способ оплаты через раздел «Спонсоры» в GS14 Admin.";
        public List<Tier> Tiers { get; set; } = [];
        public List<Membership> Memberships { get; set; } = [];
        public List<Order> Orders { get; set; } = [];
        public List<History> History { get; set; } = [];
    }
    public sealed class Tier
    {
        public string Key { get; set; } = "";
        public int Rank { get; set; }
        public string Name { get; set; } = "";
        public int Days { get; set; } = 30;
        public Dictionary<string, decimal> Prices { get; set; } = [];
        public string OocColor { get; set; } = "#75CFFF";
        public int ExtraSlots { get; set; }
        public int FlavorLength { get; set; }
        public string[] GhostThemes { get; set; } = [];
        public string[] Pets { get; set; } = [];
    }
    public sealed class Membership
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Tier Tier { get; set; } = new();
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
        public Guid GrantedBy { get; set; }
        public Guid? OrderId { get; set; }
    }
    public sealed class Order
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string PlayerName { get; set; } = "";
        public Tier Tier { get; set; } = new();
        public string Currency { get; set; } = "RUB";
        public decimal Amount { get; set; }
        public string Period { get; set; } = "month";
        public int Months { get; set; } = 1;
        public int Discount { get; set; }
        public string Status { get; set; } = "pending";
        public string KeyHash { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? HandledAt { get; set; }
        public Guid? HandledBy { get; set; }
        public string Message { get; set; } = "";
    }
    public sealed record History(string Action, Guid Actor, Guid? UserId, string Details, DateTimeOffset At);
    public sealed record SubscriptionQuote(Tier Tier, string Period, int Months, int Discount, decimal Total, decimal Monthly);
    public sealed record PublicShop(List<Tier> Tiers, List<LustRewardsStore.CatalogItem> Items, string PaymentInstructions);
}
