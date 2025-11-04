using Microsoft.AspNetCore.Mvc.RazorPages;
using HelloWorldWeb.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HelloWorldWeb.Pages
{
    public class StorageDebugModel : PageModel
    {
        private readonly SupabaseStorageService _storage;

        public StorageDebugModel(SupabaseStorageService storage = null)
        {
            _storage = storage;
        }

        public List<string> AllFiles { get; set; } = new();
        public List<string> ImageFiles { get; set; } = new();
        public string ErrorMessage { get; set; }
        public bool StorageAvailable => _storage != null;

        public async Task OnGetAsync()
        {
            if (_storage == null)
            {
                ErrorMessage = "SupabaseStorageService is not configured";
                return;
            }

            try
            {
                AllFiles = await _storage.ListFilesAsync("");
                ImageFiles = AllFiles
                    .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg") || f.EndsWith(".webp"))
                    .OrderBy(name => name)
                    .ToList();
            }
            catch (System.Exception ex)
            {
                ErrorMessage = $"Error: {ex.Message}";
            }
        }
    }
}

