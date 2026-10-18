using Content.Server.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SS14.Admin.Helpers;

namespace SS14.Admin.Pages.Rewards;

// Lust edit - выдача косметики доступна администраторам с PERMISSIONS.
[Authorize]
[ValidateAntiForgeryToken]
public sealed class IndexModel(PostgresServerDbContext db, GribleAdminAccess access, LustRewardsStore rewards, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)] public string UserId { get; set; } = "";
    [TempData] public string? Notice { get; set; }
    public string? Error { get; private set; }
    public LustRewardsStore.RewardsState State { get; private set; } = new([], [], []);
    public Dictionary<Guid, string> Players { get; private set; } = [];
    public Guid ActorId => User.Claims.GetUserId();
    public Guid? TargetId => Guid.TryParse(UserId, out var id) && id != Guid.Empty ? id : null;
    public string PlayerName(Guid id) => Players.GetValueOrDefault(id, id.ToString());
    public string ItemName(string kind, string id) => State.Items.FirstOrDefault(i => i.Kind == kind && i.PrototypeId == id)?.Name ?? id;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await access.CanManageAsync(ActorId)) return Forbid();
        Players = await db.Player.AsNoTracking().OrderBy(p => p.LastSeenUserName).ToDictionaryAsync(p => p.UserId, p => p.LastSeenUserName);
        try { State = await rewards.ReadAsync(ActorId); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            logger.LogError(e, "Unable to read GS14 rewards");
            Error = "Не удалось прочитать хранилище выдач. Проверь журнал панели; существующие данные не перезаписаны.";
        }
        if (UserId.Length > 0 && TargetId == null) Error = "Укажи корректный UUID аккаунта игрока.";
        return Page();
    }

    public async Task<IActionResult> OnPostGrantAsync(string kind, string? prototypeId, bool all, string? reason)
    {
        if (!await access.CanManageAsync(ActorId)) return Forbid();
        if (TargetId is not { } target) { Notice = "Укажи корректный UUID аккаунта игрока."; return RedirectToPage(new { UserId }); }
        try
        {
            var count = await rewards.GrantAsync(ActorId, target, kind, prototypeId, all, reason ?? "");
            Notice = count == 0 ? "Выбранный доступ уже выдан." : $"Выдано: {count}. Переоткрой игровое меню через секунду. Питомец появится при следующем появлении персонажа.";
            logger.LogInformation("GS14 rewards: {Actor} granted {Count} {Kind} items to {Target}", ActorId, count, kind, target);
        }
        catch (ArgumentException e) { Notice = e.Message; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
        {
            logger.LogError(e, "Unable to save GS14 rewards");
            Notice = "Не удалось сохранить выдачу. Подробности в журнале панели.";
        }
        return RedirectToPage(new { UserId });
    }

    public async Task<IActionResult> OnPostRevokeAsync(string kind, string prototypeId)
    {
        if (!await access.CanManageAsync(ActorId)) return Forbid();
        if (TargetId is not { } target) return BadRequest();
        try
        {
            var count = await rewards.RevokeAsync(ActorId, target, kind, prototypeId);
            Notice = count == 0 ? "Эта выдача уже отозвана." : "Выдача отозвана. Уже появившийся питомец или применённый облик останется до следующего появления; повторный выбор проверит доступ заново.";
            logger.LogInformation("GS14 rewards: {Actor} revoked {Kind}/{Item} from {Target}", ActorId, kind, prototypeId, target);
        }
        catch (ArgumentException e) { Notice = e.Message; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
        {
            logger.LogError(e, "Unable to revoke GS14 reward");
            Notice = "Не удалось сохранить отзыв. Подробности в журнале панели.";
        }
        return RedirectToPage(new { UserId });
    }
}
