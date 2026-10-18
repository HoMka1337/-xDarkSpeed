using Content.Server.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SS14.Admin.Pages.Bans.BanTemplates;

[Authorize(Roles = "BAN")]
[ValidateAntiForgeryToken]
public class Create(PostgresServerDbContext dbContext) : PageModel
{
    [BindProperty] public string Title { get; set; } = "";

    public void OnGet()
    {

    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            TempData["StatusMessage"] = "Ошибка: название не может быть пустым.";
            return Page();
        }

        var template = new BanTemplate
        {
            Title = Title,
        };

        dbContext.BanTemplate.Add(template);

        await dbContext.SaveChangesAsync();

        return RedirectToPage("View", new { id = template.Id });
    }
}
