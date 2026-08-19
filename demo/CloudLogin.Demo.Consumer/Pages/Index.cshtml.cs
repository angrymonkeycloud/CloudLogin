using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace CloudLogin.Demo.Consumer.Pages;

public class IndexModel : PageModel
{
    public bool IsAuthenticated { get; private set; }
    public string? Name { get; private set; }
    public string? Email { get; private set; }

    public void OnGet()
    {
        IsAuthenticated = User.Identity?.IsAuthenticated ?? false;
        Name = User.FindFirstValue(ClaimTypes.Name);
        Email = User.FindFirstValue(ClaimTypes.Email);
    }
}
