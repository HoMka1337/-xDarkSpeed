using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SS14.Admin.Pages;

[AllowAnonymous]
public sealed class StatusModel : PageModel
{
    public int Code { get; private set; }
    public string Heading { get; private set; } = "Не удалось открыть страницу";
    public string Description { get; private set; } = "Вернись на главную страницу и попробуй ещё раз.";
    public void OnGet(int code)
    {
        Code = code is >= 400 and <= 599 ? code : 404;
        Response.StatusCode = Code;
        (Heading, Description) = Code switch
        {
            400 => ("Проверь отправленные данные", "Форма заполнена неверно или устарела. Открой её заново и повтори отправку."),
            401 => ("Нужно войти в аккаунт", "Эта страница доступна после входа в аккаунт администратора."),
            403 => ("Недостаточно прав", "У твоего аккаунта нет разрешения открывать этот раздел или выполнять это действие."),
            404 => ("Страница не найдена", "Проверь адрес. Для просмотра заявки используй полную личную ссылку, полученную при её создании."),
            429 => ("Слишком много запросов", "Подожди минуту и попробуй ещё раз."),
            _ => ("Произошла ошибка на сайте", "Попробуй открыть страницу позже. Если ошибка повторяется, сообщи владельцу проекта.")
        };
    }
}
