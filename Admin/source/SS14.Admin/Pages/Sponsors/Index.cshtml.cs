using Content.Server.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SS14.Admin.Helpers;

namespace SS14.Admin.Pages.Sponsors;

[Authorize]
[ValidateAntiForgeryToken]
public sealed class IndexModel(PostgresServerDbContext db, GribleAdminAccess access, LustSponsorStore store, ILogger<IndexModel> logger) : PageModel
{
    public LustSponsorStore.Document State { get; private set; } = new();
    public List<LustRewardsStore.CatalogItem> Items { get; private set; } = [];
    public Dictionary<Guid, string> Players { get; private set; } = [];
    [TempData] public string? Notice { get; set; }
    public string? Error { get; private set; }
    private Guid Actor => User.Claims.GetUserId();
    public string PlayerName(Guid id) => id == Guid.Empty ? "Система" : Players.GetValueOrDefault(id, id.ToString());
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await access.CanManageAsync(Actor)) return Forbid();
        Players = await db.Player.AsNoTracking().OrderBy(p => p.LastSeenUserName).ToDictionaryAsync(p => p.UserId, p => p.LastSeenUserName);
        try { State = await store.ReadAdminAsync(Actor); Items = (await store.ReadPublicAsync()).Items; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { logger.LogError(e, "Unable to read GS14 sponsors"); Error = "Не удалось прочитать магазин. Проверь журнал панели."; }
        return Page();
    }
    public Task<IActionResult> OnPostConfirmAsync(Guid orderId) => Run(async () => await store.ConfirmAsync(Actor, orderId) ? "Оплата подтверждена, подписка активирована. Игроку нужно переподключиться." : "Эта заявка уже была подтверждена. Повторная подписка не создана.");
    public Task<IActionResult> OnPostDeclineAsync(Guid orderId, string? message) => Run(async () => { await store.DeclineAsync(Actor, orderId, message ?? ""); return "Заявка отклонена."; });
    public Task<IActionResult> OnPostGrantAsync(string userId, string tierKey, string period = "month") => Run(async () => { if (!Guid.TryParse(userId, out var id) || id == Guid.Empty) throw new ArgumentException("Укажи идентификатор игрового аккаунта."); await store.GrantAsync(Actor, id, tierKey, period); return "Подписка выдана. Повторная выдача того же уровня продлевает срок."; });
    public Task<IActionResult> OnPostRevokeAsync(Guid membershipId) => Run(async () => { await store.RevokeAsync(Actor, membershipId); return "Подписка отозвана. Отдельные бессрочные выдачи сохраняются."; });
    public Task<IActionResult> OnPostInstructionsAsync(string? instructions) => Run(async () => { await store.UpdateInstructionsAsync(Actor, instructions ?? ""); return "Инструкция по оплате сохранена."; });
    public Task<IActionResult> OnPostTierAsync(string key, string name, int days, decimal rub, decimal pln, decimal eur, string oocColor, int extraSlots, int flavorLength, string[] ghostThemes, string[] pets) => Run(async () =>
    {
        await store.UpdateTierAsync(Actor, new LustSponsorStore.Tier { Key = key, Name = name ?? "", Days = days, Prices = new() { ["RUB"] = rub, ["PLN"] = pln, ["EUR"] = eur }, OocColor = oocColor ?? "", ExtraSlots = extraSlots, FlavorLength = flavorLength, GhostThemes = ghostThemes ?? [], Pets = pets ?? [] });
        return "Уровень обновлён для новых заявок. Уже оформленные заявки и подписки сохраняют прежний состав.";
    });
    private async Task<IActionResult> Run(Func<Task<string>> action)
    {
        if (!await access.CanManageAsync(Actor)) return Forbid();
        if (!ModelState.IsValid) { Notice = "Проверь значения в форме."; return RedirectToPage(); }
        try { Notice = await action(); }
        catch (ArgumentException e) { Notice = e.Message; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { logger.LogError(e, "Unable to save GS14 sponsors"); Notice = "Не удалось сохранить изменение. Подробности в журнале панели."; }
        return RedirectToPage();
    }
}
