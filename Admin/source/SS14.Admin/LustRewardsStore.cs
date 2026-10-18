using System.Text.Json;

namespace SS14.Admin;

// Lust edit - атомарное локальное хранилище персональных выдач и истории.
public sealed class LustRewardsStore(IConfiguration config, IWebHostEnvironment env, GribleAdminAccess access)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string DirectoryPath => Path.GetFullPath(config["Grible:RewardsDirectory"] ?? "private/rewards", env.ContentRootPath);

    public async Task<RewardsState> ReadAsync(Guid actorId)
    {
        await EnsureAccessAsync(actorId);
        var catalog = await ReadFileAsync<Catalog>("catalog.json") ?? new Catalog();
        var ledger = await ReadFileAsync<Ledger>("grants.json") ?? new Ledger();
        if (catalog.Version != 1 || ledger.Version != 1)
            throw new InvalidDataException("Неизвестная версия хранилища выдач.");
        return new RewardsState(catalog.Items, ledger.Grants, ledger.History);
    }

    public async Task<int> GrantAsync(Guid actorId, Guid userId, string kind, string? prototypeId, bool all, string reason)
    {
        await EnsureAccessAsync(actorId);
        Validate(userId, kind, reason);
        return await MutateAsync(async ledger =>
        {
            var catalog = await ReadFileAsync<Catalog>("catalog.json") ?? new Catalog();
            if (catalog.Version != 1) throw new InvalidDataException("Неизвестная версия каталога.");
            var items = catalog.Items.Where(i => i.Kind == kind && i.Restricted && (all || i.PrototypeId == prototypeId)).ToList();
            if (items.Count == 0) throw new ArgumentException("Выбери доступный для выдачи предмет из каталога сервера.");
            var count = 0;
            foreach (var item in items)
            {
                if (ledger.Grants.Any(g => g.UserId == userId && g.Kind == kind && g.PrototypeId == item.PrototypeId)) continue;
                var grant = new Grant(userId, kind, item.PrototypeId, actorId, DateTimeOffset.UtcNow, reason.Trim());
                ledger.Grants.Add(grant);
                ledger.History.Add(new HistoryEntry("grant", grant));
                count++;
            }
            return count;
        });
    }

    public async Task<int> RevokeAsync(Guid actorId, Guid userId, string kind, string prototypeId)
    {
        await EnsureAccessAsync(actorId);
        Validate(userId, kind, "");
        return await MutateAsync(ledger =>
        {
            var grant = ledger.Grants.SingleOrDefault(g => g.UserId == userId && g.Kind == kind && g.PrototypeId == prototypeId);
            if (grant == null) return Task.FromResult(0);
            ledger.Grants.Remove(grant);
            ledger.History.Add(new HistoryEntry("revoke", grant with { GrantedBy = actorId, GrantedAt = DateTimeOffset.UtcNow, Reason = "Отзыв через панель" }));
            return Task.FromResult(1);
        });
    }

    private async Task EnsureAccessAsync(Guid actorId)
    {
        if (!await access.CanManageAsync(actorId)) throw new UnauthorizedAccessException();
    }

    private static void Validate(Guid userId, string kind, string reason)
    {
        if (userId == Guid.Empty || kind is not ("ghost" or "pet") || reason.Length > 200)
            throw new ArgumentException("Проверь UUID игрока, категорию и комментарий (до 200 символов).");
    }

    private async Task<int> MutateAsync(Func<Ledger, Task<int>> mutate)
    {
        Directory.CreateDirectory(DirectoryPath);
        // Файловая блокировка защищает от одновременных запросов и второго процесса панели.
        await using var gate = await AcquireLockAsync();
        var ledger = await ReadFileAsync<Ledger>("grants.json") ?? new Ledger();
        if (ledger.Version != 1) throw new InvalidDataException("Неизвестная версия хранилища выдач.");
        var changed = await mutate(ledger);
        if (changed == 0) return 0;
        var target = Path.Combine(DirectoryPath, "grants.json");
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, ledger, Json);
                await stream.FlushAsync();
                stream.Flush(true);
            }
            File.Move(temp, target, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return changed;
    }

    private async Task<FileStream> AcquireLockAsync()
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(Path.Combine(DirectoryPath, "grants.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { await Task.Delay(25); }
        }
    }

    private async Task<T?> ReadFileAsync<T>(string name) where T : class
    {
        var path = Path.Combine(DirectoryPath, name);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Хранилище выдач превышает 16 МиБ.");
        return await JsonSerializer.DeserializeAsync<T>(stream, Json) ?? throw new InvalidDataException("Хранилище выдач пустое.");
    }

    public sealed class Catalog
    {
        public int Version { get; set; } = 1;
        public List<CatalogItem> Items { get; set; } = [];
    }
    public sealed class Ledger
    {
        public int Version { get; set; } = 1;
        public List<Grant> Grants { get; set; } = [];
        public List<HistoryEntry> History { get; set; } = [];
    }
    public sealed record CatalogItem(string Kind, string PrototypeId, string Name, bool Restricted);
    public sealed record Grant(Guid UserId, string Kind, string PrototypeId, Guid GrantedBy, DateTimeOffset GrantedAt, string Reason);
    public sealed record HistoryEntry(string Action, Grant Grant);
    public sealed record RewardsState(List<CatalogItem> Items, List<Grant> Grants, List<HistoryEntry> History);
}
