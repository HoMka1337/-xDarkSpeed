using Content.Server.Database;

namespace SS14.Admin.Helpers;

public static class BanExemptions
{
    public static (ServerBanExemptFlags Value, string DisplayName)[] GetExemptions()
    {
        return
        [
            (ServerBanExemptFlags.Datacenter, "Дата-центр"),
            // (ServerBanExemptFlags.IP, "Only matches IP"),
            (ServerBanExemptFlags.BlacklistedRange, "Подсеть из чёрного списка"),
        ];
    }

    public static ServerBanExemptFlags GetExemptionFromForm(IFormCollection formCollection)
    {
        var flags = ServerBanExemptFlags.None;

        foreach (var (value, _) in GetExemptions())
        {
            if (formCollection.TryGetValue($"exemption_{value}", out var checkValue) && checkValue == "on")
            {
                flags |= value;
            }
        }

        return flags;
    }
}
