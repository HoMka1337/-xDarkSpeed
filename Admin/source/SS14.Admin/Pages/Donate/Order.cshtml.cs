using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SS14.Admin.Pages.Donate;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OrderModel(LustSponsorStore store) : PageModel
{
    public LustSponsorStore.Order Order { get; private set; } = new();
    public string Instructions { get; private set; } = "";
    public async Task<IActionResult> OnGetAsync(Guid id, string? key)
    {
        var order = await store.FindOrderAsync(id, key ?? "");
        if (order == null) return NotFound();
        Order = order;
        Instructions = (await store.ReadPublicAsync()).PaymentInstructions;
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Page();
    }
}
