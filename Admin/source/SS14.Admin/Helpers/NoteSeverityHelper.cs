using Content.Shared.Database;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SS14.Admin.Helpers;

public static class NoteSeverityHelper
{
    public static SelectListItem[] SeverityItems =>
    [
        new SelectListItem("Нет", NoteSeverity.None.ToString()),
        new SelectListItem("Низкая", NoteSeverity.Minor.ToString()),
        new SelectListItem("Средняя", NoteSeverity.Medium.ToString()),
        new SelectListItem("Высокая", NoteSeverity.High.ToString()),
    ];
}
