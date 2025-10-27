using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;

namespace HelloWorldWeb.Pages
{
    public class LogoutModel : PageModel
    {
        public IActionResult OnGet()
        {
            HttpContext.Session.Clear();
            Response.Cookies.Delete("Username");

            return RedirectToPage("/Login");
        }
        
        public IActionResult OnPost()
        {
            HttpContext.Session.Clear();
            Response.Cookies.Delete("Username");

            return RedirectToPage("/Login");
        }
    }
}