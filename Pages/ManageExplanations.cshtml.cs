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
        private readonly QuestionDifficultyService _difficultyService;

        public ManageExplanationsModel(
            SupabaseStorageService storage = null,
            ExplanationService explanationService = null,
            QuestionDifficultyService difficultyService = null)
        {
            _storage = storage;
            _explanationService = explanationService;
            _difficultyService = difficultyService;
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
            QuestionFile = QuestionFile?.Trim();
            Explanation = Explanation?.Trim();

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
            QuestionFile = QuestionFile?.Trim();

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
                var questionFiles = new List<string>();

                // Prefer loading from difficulty service (reliable list of questions)
                if (_difficultyService != null)
                {
                    try
                    {
                        var difficultyQuestions = await _difficultyService.GetAllQuestions(1000);
                        if (difficultyQuestions != null && difficultyQuestions.Any())
                        {
                            questionFiles = difficultyQuestions
                                .Select(q => q.QuestionFile)
                                .Where(q => !string.IsNullOrWhiteSpace(q))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(q => q, StringComparer.OrdinalIgnoreCase)
                                .ToList();
                        }
                    }
                    catch
                    {
                        // ignore and fallback to storage/local
                    }
                }

                // Fallback to storage bucket listing
                if (!questionFiles.Any())
                {
                    List<string> allImages = new List<string>();

                    if (_storage != null)
                    {
                        var storageImages = await _storage.ListFilesAsync("");
                        allImages = storageImages
                            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                    else
                    {
                        var imagesDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "quiz_images");
                        if (System.IO.Directory.Exists(imagesDir))
                        {
                            allImages = System.IO.Directory.GetFiles(imagesDir)
                                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                                .Select(System.IO.Path.GetFileName)
                                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                .ToList();
                        }
                    }

                    if (allImages.Any())
                    {
                        // Assume alphabetical grouping of question + answers (chunks of 5)
                        for (int i = 0; i < allImages.Count; i += 5)
                        {
                            if (i < allImages.Count)
                            {
                                questionFiles.Add(allImages[i]);
                            }
                        }
                    }
                }

                // Get all explanations
                Dictionary<string, string> explanations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (_explanationService != null)
                {
                    explanations = await _explanationService.GetAllExplanations();
                }

                if (!questionFiles.Any() && explanations.Any())
                {
                    // As a safety net, show any questions that already have explanations
                    questionFiles = explanations.Keys
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                // Build the questions list
                foreach (var qFile in questionFiles.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    Questions.Add(new QuestionExplanationInfo
                    {
                        QuestionFile = qFile.Trim(),
                        Explanation = explanations.TryGetValue(qFile.Trim(), out var exp) ? exp : null
                    });
                }

                TotalQuestions = Questions.Count;
                QuestionsWithExplanations = Questions.Count(q => q.HasExplanation);
            }
            catch (Exception ex)
            {
                Questions = new List<QuestionExplanationInfo>();
                TotalQuestions = 0;
                QuestionsWithExplanations = 0;
                TempData["ErrorMessage"] = $"שגיאה בטעינת השאלות: {ex.Message}";
            }
        }
    }
}

