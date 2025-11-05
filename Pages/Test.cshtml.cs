using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using HelloWorldWeb.Services;
using HelloWorldWeb.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace HelloWorldWeb.Pages
{
    [IgnoreAntiforgeryToken]
    public class TestModel : PageModel
    {
        private const int TotalQuestions = 17;
        private static readonly TimeSpan TestDuration = TimeSpan.FromHours(2);

        private readonly SupabaseStorageService _storage;
        private readonly QuestionStatsService _stats;
        private readonly TestSessionService _testSession;
        private readonly EmailService _email;
        private readonly QuestionDifficultyService _difficultyService;

        public TestModel(SupabaseStorageService storage = null, QuestionStatsService stats = null, TestSessionService testSession = null, EmailService email = null, QuestionDifficultyService difficultyService = null)
        {
            _storage = storage;
            _stats = stats;
            _testSession = testSession;
            _email = email;
            _difficultyService = difficultyService;
        }

        public bool AnswerChecked { get; set; }
        public bool IsCorrect { get; set; }
        public string SelectedAnswer { get; set; }
        public string QuestionImageUrl { get; set; }
        public Dictionary<string, string> ShuffledAnswers { get; set; }
        public Dictionary<string, string> AnswerImageUrls { get; set; }
        public int CurrentIndex { get; set; }
        public int DisplayQuestionNumber => CurrentIndex + 1;
        public string TestEndUtcString { get; set; }

        public class TestQuestion
        {
            public string Question { get; set; }
            public Dictionary<string, string> Answers { get; set; }
        }

        public class TestAnswer
        {
            public string SelectedKey { get; set; }
            public bool IsCorrect { get; set; }
        }

        public class TestState
        {
            public DateTime StartedUtc { get; set; }
            public List<TestQuestion> Questions { get; set; } = new List<TestQuestion>();
            public List<TestAnswer> Answers { get; set; } = new List<TestAnswer>();
            public int CurrentIndex { get; set; }
        }

        public async Task<IActionResult> OnGet()
        {
            var username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToPage("/Login");
            }

            if (_testSession == null)
            {
                return await OnGetLegacy();
            }

            var token = Request.Query["token"].ToString();
            var start = Request.Query["start"].ToString();

            TestSession session = null;

            if (!string.IsNullOrEmpty(token))
            {
                session = await _testSession.GetSession(token);
                
                if (session != null && session.Username != username)
                {
                    return RedirectToPage("/MyExams");
                }
            }

            if (session == null)
            {
                session = await _testSession.GetActiveSession(username);
            }

            if (!string.IsNullOrEmpty(start) && session != null && session.Status == "active")
            {
                TempData["ActiveTestAlert"] = "קיים מבחן פעיל! עליך לסיים אותו על מנת להתחיל מבחן חדש.";
                return RedirectToPage("/Test", new { token = session.Token });
            }

            if (!string.IsNullOrEmpty(start) || session == null)
            {
                var difficulty = Request.Query["difficulty"].ToString();
                
                var state = await BuildNewStateAsync(difficulty);
                
                var questionsJson = JsonConvert.SerializeObject(state.Questions);
                
                session = await _testSession.CreateSession(username, questionsJson);
                
                if (session == null)
                {
                    return await OnGetLegacy();
                }

                return RedirectToPage("/Test", new { token = session.Token });
            }

            if (_testSession.IsExpired(session) || session.Status != "active")
            {
                if (session.Status == "active")
                {
                    await _testSession.UpdateSessionStatus(session.Token, "expired");
                }
                return RedirectToPage("/TestResults", new { token = session.Token });
            }

            var testState = new TestState
            {
                StartedUtc = session.StartedUtc,
                Questions = JsonConvert.DeserializeObject<List<TestQuestion>>(session.QuestionsJson) ?? new List<TestQuestion>(),
                Answers = JsonConvert.DeserializeObject<List<TestAnswer>>(session.AnswersJson) ?? new List<TestAnswer>(),
                CurrentIndex = session.CurrentIndex
            };

            if (testState.CurrentIndex >= testState.Questions.Count)
            {
                await _testSession.UpdateSessionStatus(session.Token, "completed");
                return RedirectToPage("/TestResults", new { token = session.Token });
            }

            await BindCurrentAsync(testState);
            ViewData["Token"] = session.Token;
            return Page();
        }

        private async Task<IActionResult> OnGetLegacy()
        {
            var start = Request.Query["start"].ToString();
            var advance = Request.Query["advance"].ToString();
            var difficulty = Request.Query["difficulty"].ToString();

            var state = GetState();

            if (!string.IsNullOrEmpty(start) || state == null)
            {
                state = await BuildNewStateAsync(difficulty);
                SaveState(state);
            }
            else if (!string.IsNullOrEmpty(advance))
            {
                if (state.CurrentIndex < state.Answers.Count && state.Answers[state.CurrentIndex] != null)
                {
                    state.CurrentIndex = Math.Min(state.CurrentIndex + 1, state.Questions.Count);
                    SaveState(state);
                }
            }

            if (IsExpired(state) || state.CurrentIndex >= state.Questions.Count)
                return RedirectToPage("/TestResults");

            await BindCurrentAsync(state);
            return Page();
        }

        public async Task<IActionResult> OnPost()
        {
            var username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToPage("/Login");
            }

            var token = Request.Form["token"].ToString();
            
            if (_testSession == null || string.IsNullOrEmpty(token))
            {
                return OnPostLegacy();
            }

            var session = await _testSession.GetSession(token);
            if (session == null || session.Username != username)
            {
                return RedirectToPage("/MyExams");
            }

            if (_testSession.IsExpired(session) || session.Status != "active")
            {
                await _testSession.UpdateSessionStatus(session.Token, "expired");
                return RedirectToPage("/TestResults", new { token = session.Token });
            }

            var questions = JsonConvert.DeserializeObject<List<TestQuestion>>(session.QuestionsJson) ?? new List<TestQuestion>();
            var answers = JsonConvert.DeserializeObject<List<TestAnswer>>(session.AnswersJson) ?? new List<TestAnswer>();

            var selected = Request.Form["answer"].ToString();
            var idxStr = Request.Form["questionIndex"].ToString();
            int idx = session.CurrentIndex;
            int.TryParse(idxStr, out idx);
            idx = Math.Clamp(idx, 0, questions.Count - 1);

            var isCorrect = selected == "correct";

            if (answers.Count <= idx)
            {
                while (answers.Count < idx)
                    answers.Add(new TestAnswer());
                answers.Add(new TestAnswer { SelectedKey = selected, IsCorrect = isCorrect });
            }

            try 
            { 
                var qid = (idx >= 0 && idx < questions.Count) ? questions[idx].Question : null; 
                _stats?.Record(qid, isCorrect);
                
                if (!string.IsNullOrEmpty(qid) && _difficultyService != null)
                {
                    _ = _difficultyService.UpdateQuestionStats(qid, isCorrect);
                }
            } 
            catch { }

            session.CurrentIndex = Math.Min(idx + 1, questions.Count);
            session.AnswersJson = JsonConvert.SerializeObject(answers);
            session.Score = answers.Count(a => a != null && a.IsCorrect) * 6;
            session.MaxScore = questions.Count * 6;

            await _testSession.UpdateSession(session);

            if (_testSession.IsExpired(session) || session.CurrentIndex >= questions.Count)
            {
                if (session.Status == "active")
                {
                    await _testSession.UpdateSessionStatus(session.Token, "completed");
                }
                return RedirectToPage("/TestResults", new { token = session.Token });
            }

            return RedirectToPage("/Test", new { token = session.Token });
        }

        public async Task<IActionResult> OnPostEndTest()
        {
            var username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToPage("/Login");
            }

            var token = Request.Form["token"].ToString();
            
            if (_testSession != null && !string.IsNullOrEmpty(token))
            {
                var session = await _testSession.GetSession(token);
                if (session != null && session.Username == username)
                {
                    var questions = JsonConvert.DeserializeObject<List<TestQuestion>>(session.QuestionsJson) ?? new List<TestQuestion>();
                    var answers = JsonConvert.DeserializeObject<List<TestAnswer>>(session.AnswersJson) ?? new List<TestAnswer>();
                    
                    session.Status = "completed";
                    session.CompletedUtc = DateTime.UtcNow;
                    session.Score = answers.Count(a => a != null && a.IsCorrect) * 6;
                    session.MaxScore = questions.Count * 6;
                    
                    await _testSession.UpdateSession(session);
                    
                    return RedirectToPage("/TestResults", new { token = token });
                }
                else
                {
                }
            }
            else
            {
            }
            
            return RedirectToPage("/TestResults");
        }

        private IActionResult OnPostLegacy()
        {
            var state = GetState();
            if (state == null)
                return RedirectToPage("/Test", new { start = 1 });

            if (IsExpired(state) || state.CurrentIndex >= state.Questions.Count)
                return RedirectToPage("/TestResults");

            var selected = Request.Form["answer"].ToString();
            var idxStr = Request.Form["questionIndex"].ToString();
            int idx = state.CurrentIndex;
            int.TryParse(idxStr, out idx);
            idx = Math.Clamp(idx, 0, state.Questions.Count - 1);

            var q = state.Questions[idx];
            var isCorrect = selected == "correct";

            if (state.Answers.Count <= idx)
            {
                while (state.Answers.Count < idx)
                    state.Answers.Add(new TestAnswer());
                state.Answers.Add(new TestAnswer { SelectedKey = selected, IsCorrect = isCorrect });
            }

            try { var qid = (idx >= 0 && idx < state.Questions.Count) ? state.Questions[idx].Question : null; _stats?.Record(qid, isCorrect); } catch { }

            state.CurrentIndex = Math.Min(idx + 1, state.Questions.Count);
            SaveState(state);

            if (IsExpired(state) || state.CurrentIndex >= state.Questions.Count)
                return RedirectToPage("/TestResults");

            return RedirectToPage("/Test");
        }

        private bool IsExpired(TestState state)
        {
            if (state == null) return true;
            var end = state.StartedUtc.Add(TestDuration);
            return DateTime.UtcNow >= end;
        }

        private const string SessionKey = "TestStateV1";

        private TestState GetState()
        {
            try
            {
                var json = HttpContext.Session.GetString(SessionKey);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonConvert.DeserializeObject<TestState>(json);
            }
            catch { return null; }
        }

        private void SaveState(TestState state)
        {
            try
            {
                HttpContext.Session.SetString(SessionKey, JsonConvert.SerializeObject(state));
            }
            catch { }
        }

        private async Task<TestState> BuildNewStateAsync(string difficulty = null)
        {
            var state = new TestState
            {
                StartedUtc = DateTime.UtcNow,
                Questions = new List<TestQuestion>(),
                Answers = new List<TestAnswer>(),
                CurrentIndex = 0
            };

            var all = await LoadAllQuestionGroupsAsync(difficulty);
            FisherYatesShuffle(all);
            foreach (var g in all.Take(TotalQuestions))
            {
                var correct = g[1];
                var wrong = g.Skip(2).Take(3).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                var answers = new List<(string key, string img)>
                {
                    ("correct", correct)
                };
                if (wrong.Count > 0) answers.Add(("a", wrong[0]));
                if (wrong.Count > 1) answers.Add(("b", wrong[1]));
                if (wrong.Count > 2) answers.Add(("c", wrong[2]));
                answers = answers.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToList();

                state.Questions.Add(new TestQuestion
                {
                    Question = g[0],
                    Answers = answers.ToDictionary(x => x.key, x => x.img)
                });
            }

            return state;
        }

        private async Task<List<List<string>>> LoadAllQuestionGroupsAsync(string difficulty = null)
        {
            List<string> allImages;
            if (_storage != null)
            {
                var images = await _storage.ListFilesAsync("");
                allImages = images
                    .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg") || f.EndsWith(".webp"))
                    .OrderBy(name => name)
                    .ToList();
            }
            else
            {
                var imagesDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "quiz_images");
                if (!System.IO.Directory.Exists(imagesDir))
                    return new List<List<string>>();

                allImages = System.IO.Directory.GetFiles(imagesDir)
                    .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg") || f.EndsWith(".webp"))
                    .Select(System.IO.Path.GetFileName)
                    .OrderBy(name => name)
                    .ToList();
            }

            var grouped = new List<List<string>>();

            if (!string.IsNullOrEmpty(difficulty))
            {
                var allowedQuestions = await LoadDifficultyQuestionsAsync(difficulty);
                if (allowedQuestions != null && allowedQuestions.Any())
                {
                    foreach (var questionFile in allowedQuestions)
                    {
                        if (string.IsNullOrWhiteSpace(questionFile))
                            continue;
                        
                        int idx = allImages.IndexOf(questionFile);
                        
                        if (idx < 0)
                        {
                            idx = allImages.FindIndex(img => 
                                string.Equals(img, questionFile, StringComparison.OrdinalIgnoreCase));
                        }
                        
                        if (idx < 0)
                        {
                            var trimmed = questionFile.Trim();
                            idx = allImages.FindIndex(img => 
                                img.Trim().Equals(trimmed, StringComparison.OrdinalIgnoreCase));
                        }
                        
                        if (idx >= 0 && idx + 4 < allImages.Count)
                        {
                            var group = allImages.GetRange(idx, 5);
                            grouped.Add(group);
                        }
                    }
                    
                    return grouped;
                }
            }

            for (int i = 0; i + 4 < allImages.Count; i += 5)
                grouped.Add(allImages.GetRange(i, 5));
            
            return grouped;
        }

        private async Task<List<string>> LoadDifficultyQuestionsAsync(string difficulty)
        {
            try
            {
                if (_difficultyService != null)
                {
                    var questions = await _difficultyService.GetQuestionsByDifficulty(difficulty);
                    
                    if (questions != null && questions.Any())
                    {
                        return questions;
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private async Task BindCurrentAsync(TestState state)
        {
            CurrentIndex = Math.Clamp(state.CurrentIndex, 0, Math.Max(0, state.Questions.Count - 1));
            var q = state.Questions[CurrentIndex];

            ShuffledAnswers = q.Answers;
            var answers = q.Answers ?? new Dictionary<string, string>();

            if (_storage != null)
            {
                var paths = new List<string> { q.Question };
                paths.AddRange(answers.Values.Where(v => !string.IsNullOrWhiteSpace(v)));
                var signed = await _storage.GetSignedUrlsAsync(paths);
                QuestionImageUrl = signed.TryGetValue(q.Question, out var qu) ? qu : string.Empty;
                AnswerImageUrls = new Dictionary<string, string>();
                foreach (var kv in answers)
                {
                    if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                    AnswerImageUrls[kv.Key] = signed.TryGetValue(kv.Value, out var au) ? au : string.Empty;
                }
            }
            else
            {
                QuestionImageUrl = string.IsNullOrWhiteSpace(q.Question) ? string.Empty : ($"/quiz_images/{q.Question}");
                AnswerImageUrls = new Dictionary<string, string>();
                foreach (var kv in answers)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Value))
                        AnswerImageUrls[kv.Key] = $"/quiz_images/{kv.Value}";
                }
            }

            var end = state.StartedUtc.Add(TestDuration);
            TestEndUtcString = end.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        private static void FisherYatesShuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}

