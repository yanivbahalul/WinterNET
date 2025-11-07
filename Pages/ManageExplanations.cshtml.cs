using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using HelloWorldWeb.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HelloWorldWeb.Pages
{
    public class ManageExplanationsModel : PageModel
    {
        private readonly SupabaseStorageService _storage;
        private readonly ExplanationService _explanationService;

        public ManageExplanationsModel(SupabaseStorageService storage = null, ExplanationService explanationService = null)
        {
            _storage = storage;
            _explanationService = explanationService;
        }

        public class QuestionExplanationInfo
        {
            public string QuestionFile { get; set; }
            public string Explanation { get; set; }
            public bool HasExplanation => !string.IsNullOrWhiteSpace(Explanation);
        }

        public List<QuestionExplanationInfo> Questions { get; set; } = new List<QuestionExplanationInfo>();
        public int QuestionsWithExplanations { get; set; }
        public int TotalQuestions { get; set; }

        [BindProperty]
        public string QuestionFile { get; set; }

        [BindProperty]
        public string Explanation { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadQuestionsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            if (string.IsNullOrWhiteSpace(QuestionFile))
            {
                TempData["ErrorMessage"] = "לא נבחרה שאלה";
                await LoadQuestionsAsync();
                return Page();
            }

            if (_explanationService == null)
            {
                TempData["ErrorMessage"] = "שירות ההסברים אינו זמין";
                await LoadQuestionsAsync();
                return Page();
            }

            try
            {
                var success = await _explanationService.SaveExplanation(QuestionFile, Explanation);
                if (success)
                {
                    TempData["SuccessMessage"] = $"ההסבר לשאלה '{QuestionFile}' נשמר בהצלחה!";
                }
                else
                {
                    TempData["ErrorMessage"] = "שגיאה בשמירת ההסבר";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"שגיאה: {ex.Message}";
            }

            await LoadQuestionsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync()
        {
            if (string.IsNullOrWhiteSpace(QuestionFile))
            {
                TempData["ErrorMessage"] = "לא נבחרה שאלה";
                await LoadQuestionsAsync();
                return Page();
            }

            if (_explanationService == null)
            {
                TempData["ErrorMessage"] = "שירות ההסברים אינו זמין";
                await LoadQuestionsAsync();
                return Page();
            }

            try
            {
                var success = await _explanationService.DeleteExplanation(QuestionFile);
                if (success)
                {
                    TempData["SuccessMessage"] = $"ההסבר לשאלה '{QuestionFile}' נמחק בהצלחה!";
                }
                else
                {
                    TempData["ErrorMessage"] = "שגיאה במחיקת ההסבר";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"שגיאה: {ex.Message}";
            }

            await LoadQuestionsAsync();
            return Page();
        }

        private async Task LoadQuestionsAsync()
        {
            Questions = new List<QuestionExplanationInfo>();

            try
            {
                // Get all question files
                List<string> allQuestions = new List<string>();
                
                if (_storage != null)
                {
                    var allImages = await _storage.ListFilesAsync("");
                    allQuestions = allImages
                        .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg") || f.EndsWith(".webp"))
                        .OrderBy(f => f)
                        .ToList();
                }
                else
                {
                    var imagesDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "quiz_images");
                    if (System.IO.Directory.Exists(imagesDir))
                    {
                        allQuestions = System.IO.Directory.GetFiles(imagesDir)
                            .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg") || f.EndsWith(".webp"))
                            .Select(System.IO.Path.GetFileName)
                            .OrderBy(f => f)
                            .ToList();
                    }
                }

                // Extract only question files (every 5th image)
                var questionFiles = new List<string>();
                for (int i = 0; i < allQuestions.Count; i += 5)
                {
                    if (i < allQuestions.Count)
                    {
                        questionFiles.Add(allQuestions[i]);
                    }
                }

                // Get all explanations
                Dictionary<string, string> explanations = new Dictionary<string, string>();
                if (_explanationService != null)
                {
                    explanations = await _explanationService.GetAllExplanations();
                }

                // Build the questions list
                foreach (var qFile in questionFiles)
                {
                    Questions.Add(new QuestionExplanationInfo
                    {
                        QuestionFile = qFile,
                        Explanation = explanations.TryGetValue(qFile, out var exp) ? exp : null
                    });
                }

                TotalQuestions = Questions.Count;
                QuestionsWithExplanations = Questions.Count(q => q.HasExplanation);
            }
            catch (Exception)
            {
                Questions = new List<QuestionExplanationInfo>();
                TotalQuestions = 0;
                QuestionsWithExplanations = 0;
            }
        }
    }
}

