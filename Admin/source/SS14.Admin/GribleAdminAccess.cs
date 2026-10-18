using Content.Server.Database;
using Microsoft.EntityFrameworkCore;
using SS14.Admin.Admins;
using DbAdmin = Content.Server.Database.Admin;

namespace SS14.Admin;

public sealed class GribleAdminAccess(PostgresServerDbContext db, IConfiguration config, ILogger<GribleAdminAccess> logger)
{
    public Guid? OwnerId => Guid.TryParse(config["Grible:OwnerUserId"], out var id) && id != Guid.Empty ? id : null;

    public async Task<DbAdmin?> FindAsync(Guid id) => await db.Admin
        .Include(a => a.AdminRank).ThenInclude(r => r!.Flags)
        .Include(a => a.Flags).AsSplitQuery().SingleOrDefaultAsync(a => a.UserId == id);

    public async Task<bool> CanManageAsync(Guid id)
    {
        var admin = await FindAsync(id);
        return admin is { Suspended: false } && AdminHelper.GetStringFlags(admin).Contains("PERMISSIONS");
    }

    public async Task<DbAdmin?> BootstrapOwnerAsync(Guid authenticatedId)
    {
        // Первый доступ получает только UUID, заранее указанный владельцем в локальном конфиге.
        if (authenticatedId != OwnerId)
            return null;
        if (await FindAsync(authenticatedId) is { } existing)
            return existing;
        var admin = NewAdmin(authenticatedId, "Владелец Grible Station");
        db.Admin.Add(admin);
        await db.SaveChangesAsync();
        logger.LogInformation("Initial configured owner access created for {UserId}", authenticatedId);
        return admin;
    }

    public async Task GrantAsync(Guid actorId, Guid targetId, string title)
    {
        if (!await CanManageAsync(actorId))
            throw new UnauthorizedAccessException();
        var admin = await FindAsync(targetId);
        if (admin == null)
        {
            admin = NewAdmin(targetId, title);
            db.Admin.Add(admin);
        }
        else
        {
            db.RemoveRange(admin.Flags);
            admin.AdminRankId = null;
            admin.AdminRank = null;
            admin.Title = title;
            admin.Suspended = false;
            admin.Deadminned = false;
            admin.Flags = FullFlags(targetId);
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Administrator {ActorId} granted full access to {TargetId}", actorId, targetId);
    }

    public async Task<bool> RevokeAsync(Guid actorId, Guid targetId)
    {
        if (!await CanManageAsync(actorId))
            throw new UnauthorizedAccessException();
        if (targetId == actorId || targetId == OwnerId)
            return false;
        if (await FindAsync(targetId) is { } admin)
        {
            db.Admin.Remove(admin);
            await db.SaveChangesAsync();
            logger.LogInformation("Administrator {ActorId} revoked access from {TargetId}", actorId, targetId);
        }
        return true;
    }

    private static DbAdmin NewAdmin(Guid id, string title) => new()
    {
        UserId = id, Title = title, Flags = FullFlags(id)
    };

    private static List<AdminFlag> FullFlags(Guid id) => AdminFlagsHelper.AllFlags
        .Select(flag => new AdminFlag { AdminId = id, Flag = flag.ToString().ToUpperInvariant() }).ToList();
}
