using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using HelloWorldWeb.Models;
using HelloWorldWeb.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;

namespace HelloWorldWeb.Pages
{
    [IgnoreAntiforgeryToken]
    public class IndexModel : PageModel
    {
        private readonly AuthService _authService;
        private readonly EmailService _emailService;

        public IndexModel(AuthService authService, EmailService emailService)
        {
            _authService = authService;
            _emailService = emailService;
        }

        public bool AnswerChecked { get; set; }
        public bool IsCorrect { get; set; }
        public string SelectedAnswer { get; set; }
        public string QuestionImage { get; set; }
        public Dictionary<string, string> ShuffledAnswers { get; set; }
        public string Username { get; set; }
        public string ConnectionStatus { get; set; }
        public int OnlineCount { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            Username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(Username))
                return RedirectToPage("/Login");

            if (HttpContext.Session.GetString("SessionStart") == null)
            {
                HttpContext.Session.SetString("SessionStart", DateTime.UtcNow.ToString());
                HttpContext.Session.SetInt32("RapidTotal", 0);
                HttpContext.Session.SetInt32("RapidCorrect", 0);
            }

            // Optimize: Only update LastSeen once every 30 seconds
            var lastUpdateStr = HttpContext.Session.GetString("LastDbUpdate");
            var shouldUpdate = string.IsNullOrEmpty(lastUpdateStr) || 
                              (DateTime.UtcNow - DateTime.Parse(lastUpdateStr)).TotalSeconds > 30;

            if (shouldUpdate)
            {
                var user = await _authService.GetUser(Username);
                if (user != null)
                {
                    if (user.IsBanned)
                    {
                        HttpContext.Session.Clear();
                        Response.Cookies.Delete("Username");
                        return RedirectToPage("/Login");
                    }
                    user.LastSeen = DateTime.UtcNow;
                    await _authService.UpdateUser(user);
                }

                // Cache the online count
                var allUsers = await _authService.GetAllUsers();
                OnlineCount = allUsers.Count(u => u.LastSeen != null && u.LastSeen > DateTime.UtcNow.AddMinutes(-5));
                HttpContext.Session.SetString("OnlineCount", OnlineCount.ToString());
                HttpContext.Session.SetString("LastDbUpdate", DateTime.UtcNow.ToString());
            }
            else
            {
                // Use cached value
                var cachedCount = HttpContext.Session.GetString("OnlineCount");
                OnlineCount = int.TryParse(cachedCount, out var count) ? count : 0;
            }

            ConnectionStatus = "✅ Supabase connection OK";

            LoadRandomQuestion();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (Request.Form.ContainsKey("logout"))
            {
                HttpContext.Session.Clear();
                Response.Cookies.Delete("Username");
                return RedirectToPage("/Index");
            }

            Username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrEmpty(Username))
                return RedirectToPage("/Login");

            var user = await _authService.GetUser(Username);
            if (user == null)
                return RedirectToPage("/Login");

            if (user.IsBanned)
            {
                HttpContext.Session.Clear();
                Response.Cookies.Delete("Username");
                return RedirectToPage("/Login");
            }

            if (Request.Form.ContainsKey("reset"))
            {
                user.CorrectAnswers = 0;
                user.TotalAnswered = 0;
                user.IsCheater = false;
                await _authService.UpdateUser(user);
                return RedirectToPage("/Index");
            }

            var answer = Request.Form["answer"];
            var questionImage = Request.Form["questionImage"];
            var answersJson = Request.Form["answersJson"];

            if (string.IsNullOrEmpty(answersJson))
            {
                LoadRandomQuestion();
                return Page();
            }

            SelectedAnswer = answer;
            AnswerChecked = true;
            QuestionImage = questionImage;
            ShuffledAnswers = JsonConvert.DeserializeObject<Dictionary<string, string>>(answersJson);
            IsCorrect = answer == "correct";

            user.TotalAnswered++;
            if (IsCorrect)
            {
                user.CorrectAnswers++;
                // לא להזיז תמונות - לתת למשתמש לחזור על אותן שאלות
                // MoveCorrectImages();
            }

            await _authService.UpdateUser(user);

            var sessionStartStr = HttpContext.Session.GetString("SessionStart");
            DateTime.TryParse(sessionStartStr, out var sessionStart);
            var now = DateTime.UtcNow;
            var elapsedSeconds = (now - sessionStart).TotalSeconds;

            var rapidTotal = HttpContext.Session.GetInt32("RapidTotal") ?? 0;
            var rapidCorrect = HttpContext.Session.GetInt32("RapidCorrect") ?? 0;

            if (elapsedSeconds <= 200)
            {
                HttpContext.Session.SetInt32("RapidTotal", rapidTotal + 1);
                if (IsCorrect)
                    HttpContext.Session.SetInt32("RapidCorrect", rapidCorrect + 1);
            }
            else
            {
                HttpContext.Session.SetString("SessionStart", now.ToString());
                HttpContext.Session.SetInt32("RapidTotal", 1);
                HttpContext.Session.SetInt32("RapidCorrect", IsCorrect ? 1 : 0);
            }

            rapidTotal = HttpContext.Session.GetInt32("RapidTotal") ?? 0;
            rapidCorrect = HttpContext.Session.GetInt32("RapidCorrect") ?? 0;

            int cheaterCount = HttpContext.Session.GetInt32("CheaterCount") ?? 0;

            if (rapidTotal >= 10 || rapidCorrect >= 8)
            {
                Console.WriteLine($"[CHEATER DETECTED] User: {user.Username} | RapidTotal: {rapidTotal} | RapidCorrect: {rapidCorrect}");
                user.CorrectAnswers = 0;
                user.TotalAnswered = 0;
                user.IsCheater = true;
                await _authService.UpdateUser(user);

                cheaterCount++;
                HttpContext.Session.SetInt32("CheaterCount", cheaterCount);

                if (cheaterCount >= 3)
                {
                    user.IsBanned = true;
                    await _authService.UpdateUser(user);
                    HttpContext.Session.Clear();
                    Response.Cookies.Delete("Username");
                    return RedirectToPage("/Login");
                }

                HttpContext.Session.SetInt32("RapidTotal", 0);
                HttpContext.Session.SetInt32("RapidCorrect", 0);
                return RedirectToPage("/Cheater");
            }

            // Use cached online count instead of querying every time
            var cachedOnlineCount = HttpContext.Session.GetString("OnlineCount");
            OnlineCount = int.TryParse(cachedOnlineCount, out var count) ? count : 0;

            return Page();
        }

        private void MoveCorrectImages()
        {
            var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            
            // בדיקה אם קיימת תיקיית quiz_images או images
            var imagesPath = Path.Combine(wwwroot, "quiz_images");
            if (!Directory.Exists(imagesPath))
            {
                imagesPath = Path.Combine(wwwroot, "images");
            }
            
            var correctPath = Path.Combine(wwwroot, "correct_answers");

            if (!Directory.Exists(correctPath))
                Directory.CreateDirectory(correctPath);

            // הסרת הנתיב המיותרים מהשמות
            var allFiles = new[] {
                QuestionImage,
                ShuffledAnswers["correct"],
                ShuffledAnswers["a"],
                ShuffledAnswers["b"],
                ShuffledAnswers["c"]
            }.Select(f => Path.GetFileName(f)).ToArray();

            foreach (var file in allFiles)
            {
                var source = Path.Combine(imagesPath, file);
                var dest = Path.Combine(correctPath, file);
                if (System.IO.File.Exists(source) && !System.IO.File.Exists(dest))
                    System.IO.File.Move(source, dest);
            }
        }

        private void LoadRandomQuestion()
        {
            // ניסיון לטעון מהמאגר החדש - quiz_images
            var imagesDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "quiz_images");
            
            // אם אין תיקייה כזו, חזור לתיקייה הישנה (images) לשם תאימות
            if (!Directory.Exists(imagesDir))
            {
                imagesDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images");
            }

            var allImages = Directory.GetFiles(imagesDir)
                .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg") || f.EndsWith(".webp"))
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToList();

            var grouped = new List<List<string>>();
            for (int i = 0; i + 4 < allImages.Count; i += 5)
                grouped.Add(allImages.GetRange(i, 5));

            if (grouped.Count == 0)
            {
                QuestionImage = "placeholder.jpg";
                ShuffledAnswers = new Dictionary<string, string>();
                return;
            }

            var chosen = grouped[new Random().Next(grouped.Count)];
            QuestionImage = chosen[0];
            var correct = chosen[1];
            var wrong = chosen.Skip(2).Take(3).ToList();

            // קביעת נתיב התמונות - quiz_images או images
            var imageBasePath = Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "quiz_images")) 
                ? "quiz_images" 
                : "images";

            ShuffledAnswers = new List<(string, string)>
            {
                ("correct", $"{imageBasePath}/{correct}"),
                ("a", $"{imageBasePath}/{wrong[0]}"),
                ("b", $"{imageBasePath}/{wrong[1]}"),
                ("c", $"{imageBasePath}/{wrong[2]}")
            }
            .OrderBy(x => Guid.NewGuid())
            .ToDictionary(x => x.Item1, x => x.Item2);
            
            // עדכון QuestionImage עם הנתיב הנכון
            QuestionImage = $"{imageBasePath}/{QuestionImage}";
        }

        public async Task<IActionResult> OnPostReportErrorAsync()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("[OnPostReportErrorAsync] START - Report received");
            Console.WriteLine("========================================");
            
            try
            {
                Console.WriteLine("[OnPostReportErrorAsync] Reading request body...");
                string body;
                using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                    body = await reader.ReadToEndAsync();
                
                Console.WriteLine($"[OnPostReportErrorAsync] Body length: {body?.Length ?? 0} chars");
                
                if (string.IsNullOrWhiteSpace(body))
                {
                    Console.WriteLine("[OnPostReportErrorAsync] ❌ Empty body received");
                    return new JsonResult(new { error = "Empty body" }) { StatusCode = 400 };
                }

                Console.WriteLine("[OnPostReportErrorAsync] Parsing JSON data...");
                var data = Newtonsoft.Json.Linq.JObject.Parse(body);
                var questionImage = data["questionImage"]?.ToString();
                var answersJson = data["answers"]?.ToString();
                var correctAnswer = data["correctAnswer"]?.ToString();
                var explanation = data["explanation"]?.ToString();
                var selectedAnswer = data["selectedAnswer"]?.ToString();
                var username = HttpContext.Session.GetString("Username") ?? "Unknown";
                var timestamp = DateTime.UtcNow;

                Console.WriteLine("[OnPostReportErrorAsync] Report data:");
                Console.WriteLine($"  - Username: {username}");
                Console.WriteLine($"  - QuestionImage: {questionImage}");
                Console.WriteLine($"  - CorrectAnswer: {correctAnswer}");
                Console.WriteLine($"  - SelectedAnswer: {selectedAnswer}");
                Console.WriteLine($"  - Explanation: {explanation}");
                Console.WriteLine($"  - Timestamp: {timestamp:yyyy-MM-dd HH:mm:ss}");

                // Parse answers to get A, B, C, D
                Dictionary<string, string> answersDict = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(answersJson))
                    {
                        answersDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(answersJson);
                    }
                }
                catch { }

                // Build answers list: A = correct (always), B/C/D = wrong answers
                string answersList = "";
                if (answersDict != null)
                {
                    var correctAnswerFile = answersDict.ContainsKey("correct") ? Path.GetFileName(answersDict["correct"] ?? "") : "";
                    var wrongA = answersDict.ContainsKey("a") ? Path.GetFileName(answersDict["a"] ?? "") : "";
                    var wrongB = answersDict.ContainsKey("b") ? Path.GetFileName(answersDict["b"] ?? "") : "";
                    var wrongC = answersDict.ContainsKey("c") ? Path.GetFileName(answersDict["c"] ?? "") : "";
                    
                    answersList = $@"
            <span style='display: inline-block; width: 30px; color: #28a745;'><strong>A:</strong></span> <span style='color: #28a745;'>{System.Net.WebUtility.HtmlEncode(correctAnswerFile)}</span><br/>
            <span style='display: inline-block; width: 30px;'><strong>B:</strong></span> {System.Net.WebUtility.HtmlEncode(wrongA)}<br/>
            <span style='display: inline-block; width: 30px;'><strong>C:</strong></span> {System.Net.WebUtility.HtmlEncode(wrongB)}<br/>
            <span style='display: inline-block; width: 30px;'><strong>D:</strong></span> {System.Net.WebUtility.HtmlEncode(wrongC)}<br/>";
                }

                // Build pretty HTML mail body with emojis
                var htmlBody = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; background-color: #f9f9f9;'>
    <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 20px; border-radius: 10px 10px 0 0; text-align: center;'>
        <h2 style='color: white; margin: 0;'>📩 דיווח חדש התקבל מהמערכת</h2>
    </div>
    
    <div style='background-color: white; padding: 25px; border-radius: 0 0 10px 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1);'>
        <p style='font-size: 16px; color: #333; line-height: 1.8;'>
            <strong>👤 משתמש:</strong> {System.Net.WebUtility.HtmlEncode(username)}<br/>
            <strong>🕓 תאריך:</strong> {timestamp:yyyy-MM-dd HH:mm:ss}<br/>
        </p>
        
        <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;'/>
        
        <p style='font-size: 16px; color: #333;'>
            <strong>❓ שאלה:</strong> {System.Net.WebUtility.HtmlEncode(Path.GetFileName(questionImage ?? ""))}<br/>
        </p>
        
        <p style='font-size: 15px; color: #555; margin-top: 15px;'>
            <strong>📝 תשובות אפשריות:</strong><br/><br/>
            {answersList}
        </p>
        
        <p style='font-size: 16px; color: #333; margin-top: 15px;'>
            <strong>❌ תשובה שסומנה על ידי המשתמש:</strong> {System.Net.WebUtility.HtmlEncode((selectedAnswer ?? "לא סומנה").ToUpper())}<br/>
        </p>
        
        {(!string.IsNullOrWhiteSpace(explanation) ? $@"
        <div style='background-color: #fff3cd; border-right: 4px solid #ffc107; padding: 15px; margin-top: 20px; border-radius: 5px;'>
            <strong>💬 סיבה:</strong> {System.Net.WebUtility.HtmlEncode(explanation)}
        </div>" : "")}
        
        <hr style='border: none; border-top: 1px solid #eee; margin: 25px 0;'/>
        
        <p style='text-align: center; color: #888; font-size: 14px;'>
            <strong>מערכת: WinterNET</strong><br/>
            🎮 Find your limits. Or crash into them.
        </p>
    </div>
</div>";

                Console.WriteLine("[OnPostReportErrorAsync] Checking EmailService...");
                if (_emailService == null)
                {
                    Console.WriteLine("[OnPostReportErrorAsync] ❌ EmailService is NULL!");
                }
                else
                {
                    Console.WriteLine($"[OnPostReportErrorAsync] EmailService exists, IsConfigured: {_emailService.IsConfigured}");
                }

                Console.WriteLine("[OnPostReportErrorAsync] Attempting to send email...");
                var sent = _emailService?.Send($"[WinterNET] דיווח טעות — {username}", htmlBody) ?? false;
                
                Console.WriteLine("========================================");
                Console.WriteLine($"[OnPostReportErrorAsync] RESULT: Mail sent = {sent}");
                Console.WriteLine("========================================");

                return new JsonResult(new { success = true, emailSent = sent });
            }
            catch (Exception ex)
            {
                Console.WriteLine("========================================");
                Console.WriteLine($"[OnPostReportErrorAsync] ❌ ERROR occurred!");
                Console.WriteLine($"  - Exception type: {ex.GetType().Name}");
                Console.WriteLine($"  - Message: {ex.Message}");
                Console.WriteLine($"  - StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"  - Inner Exception: {ex.InnerException.Message}");
                }
                Console.WriteLine("========================================");
                return new JsonResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }
    }
}
