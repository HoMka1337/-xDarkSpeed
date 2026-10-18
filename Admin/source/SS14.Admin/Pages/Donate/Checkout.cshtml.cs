using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SS14.Admin.Pages.Donate;

[AllowAnonymous]
[ValidateAntiForgeryToken]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("sponsor-orders")]
public sealed class CheckoutModel(LustSponsorStore store) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Tier { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Currency { get; set; } = "RUB";
    [BindProperty(SupportsGet = true)] public string Period { get; set; } = "month";
    [BindProperty] public string UserId { get; set; } = "";
    [BindProperty] public string PlayerName { get; set; } = "";
    public LustSponsorStore.Tier? Selected { get; private set; }
    public LustSponsorStore.SubscriptionQuote? Quote { get; private set; }
    public string? Error { get; private set; }
    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();
    private async Task<bool> LoadAsync()
    {
        if (!LustSponsorStore.Currencies.Contains(Currency)) Currency = "RUB";
        if (!LustSponsorStore.Periods.Contains(Period)) return false;
        Selected = (await store.ReadPublicAsync()).Tiers.SingleOrDefault(t => t.Key == Tier);
        if (Selected != null) Quote = LustSponsorStore.Quote(Selected, Currency, Period);
        return Selected != null;
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (!Guid.TryParse(UserId, out var id) || id == Guid.Empty) { Error = "Введи корректный идентификатор игрового аккаунта."; return Page(); }
        try
        {
            var order = await store.CreateOrderAsync(id, PlayerName ?? "", Tier, Currency, Period);
            return RedirectToPage("./Order", new { id = order.Id, key = order.Key });
        }
        catch (ArgumentException e) { Error = e.Message; return Page(); }
    }
}
