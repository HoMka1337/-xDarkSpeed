using Content.Server.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SS14.Admin.Helpers;

namespace SS14.Admin.Pages.Administrators;

[Authorize]
[ValidateAntiForgeryToken]
public sealed class IndexModel(PostgresServerDbContext db, GribleAdminAccess access) : PageModel
{
    public List<Content.Server.Database.Admin> Admins { get; private set; } = [];
    public List<PlayerChoice> Players { get; private set; } = [];
    [BindProperty] public string UserId { get; set; } = "";
    [BindProperty] public string Title { get; set; } = "Администратор";
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await access.CanManageAsync(User.Claims.GetUserId())) return Forbid();
        Admins = await db.Admin.AsNoTracking().OrderBy(a => a.Title).ToListAsync();
        Players = await db.Player.AsNoTracking().OrderBy(p => p.LastSeenUserName)
            .Select(p => new PlayerChoice(p.UserId, p.LastSeenUserName)).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostGrantAsync()
    {
        var actorId = User.Claims.GetUserId();
        if (!await access.CanManageAsync(actorId)) return Forbid();
        if (!Guid.TryParse(UserId, out var targetId) || targetId == Guid.Empty || Title.Length > 80)
        {
            Notice = "Укажи корректный UUID аккаунта и звание до 80 символов.";
            return RedirectToPage();
        }
        await access.GrantAsync(actorId, targetId, Title.Trim());
        Notice = "Полные права выданы. Переподключись к игровому серверу; для обновления прав сайта войди заново.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid targetId)
    {
        var actorId = User.Claims.GetUserId();
        if (!await access.CanManageAsync(actorId)) return Forbid();
        Notice = await access.RevokeAsync(actorId, targetId)
            ? "Доступ отозван. Изменение игровых прав применяется после переподключения."
            : "Нельзя отозвать собственный доступ или доступ настроенного владельца.";
        return RedirectToPage();
    }

    public sealed record PlayerChoice(Guid UserId, string Name);
}
