using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SS14.Admin.Pages.Donate;

// Lust edit - открытая витрина не использует административную авторизацию.
[AllowAnonymous]
public sealed class IndexModel(LustSponsorStore store) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Currency { get; set; } = "RUB";
    [BindProperty(SupportsGet = true)] public string Period { get; set; } = "month";
    public LustSponsorStore.PublicShop Shop { get; private set; } = new([], [], "");
    public async Task OnGetAsync()
    {
        if (!LustSponsorStore.Currencies.Contains(Currency)) Currency = "RUB";
        if (!LustSponsorStore.Periods.Contains(Period)) Period = "month";
        Shop = await store.ReadPublicAsync();
    }
    public string ItemName(string kind, string id) => Shop.Items.FirstOrDefault(i => i.Kind == kind && i.PrototypeId == id)?.Name ?? id;
    public string Banner(int rank) => rank switch { 1 => "beta", 2 => "alpha", 3 => "delta", 4 => "gamma", _ => "epsilon" };
    public bool IncludesPreviousCosmetics(LustSponsorStore.Tier tier) => tier.Rank > 1 && Shop.Tiers.Where(t => t.Rank < tier.Rank).All(t => t.GhostThemes.All(tier.GhostThemes.Contains) && t.Pets.All(tier.Pets.Contains));
}
