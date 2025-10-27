using Microsoft.AspNetCore.Mvc.RazorPages;
using HelloWorldWeb.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HelloWorldWeb.Pages
{
    public class LeaderboardModel : PageModel
    {
        private readonly AuthService _authService;

        public LeaderboardModel(AuthService authService)
        {
            _authService = authService;
        }

        public List<User> SortedUsers { get; set; } = new();

        public async Task OnGetAsync()
        {
            // Optimized: Fetch top 50 users with specific columns only
            // This reduces network traffic and memory usage
            var users = await _authService.GetTopUsers(50);
            SortedUsers = users;
        }
    }
}
