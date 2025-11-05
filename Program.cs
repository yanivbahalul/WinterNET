using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using HelloWorldWeb.Models;
using HelloWorldWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Response Compression for better performance
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = new[]
    {
        "text/html",
        "text/css",
        "text/javascript",
        "application/javascript",
        "application/json",
        "image/svg+xml"
    };
});

// Memory Cache for admin data
builder.Services.AddMemoryCache();

// Razor Pages + Session
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor(); 
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<EmailService>();

// Question Stats Service (local file-based)
builder.Services.AddSingleton<QuestionStatsService>(sp =>
{
    var statsPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "question_stats.json");
    return new QuestionStatsService(statsPath);
});

// Test Session Service (Supabase)
builder.Services.AddScoped<TestSessionService>();

// Question Difficulty Service (Supabase)
builder.Services.AddScoped<QuestionDifficultyService>();

// Supabase Storage Service (optional - if using Supabase for images)
builder.Services.AddSingleton<SupabaseStorageService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var url = config["SUPABASE_URL"];
    var key = config["SUPABASE_KEY"];
    var bucket = config["SUPABASE_BUCKET"] ?? "winternet_question";
    
    // Only create if config is available
    if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
    {
        Console.WriteLine("⚠️ Supabase Storage not configured - will use local files");
        return null;
    }
    
    Console.WriteLine($"✅ Supabase Storage configured: bucket='{bucket}'");
    return new SupabaseStorageService(url, key, bucket, 3600);
});

builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".WinterNET.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.IdleTimeout = TimeSpan.FromHours(1);
});

// מדיניות עוגיות
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;
    options.Secure = CookieSecurePolicy.SameAsRequest;
});

// ביטול לוגים מיותרים
builder.Logging.ClearProviders();

var app = builder.Build();

app.UseResponseCompression(); // Enable compression first
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCookiePolicy();
app.UseSession();
app.UseAuthorization();
app.MapRazorPages();

// ✅ מסלול למחיקת session + cookie
app.MapPost("/clear-session", async context =>
{
    context.Session.Clear();
    context.Response.Cookies.Delete("Username");
    context.Response.StatusCode = 200;
    await context.Response.CompleteAsync();
});

// ✅ API endpoints
app.MapGet("/api/leaderboard-data", async context =>
{
    try
    {
        var authService = context.RequestServices.GetService<AuthService>();
        if (authService == null)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("AuthService not available");
            return;
        }

        var currentUsername = context.Session.GetString("Username") ?? "";
        var topUsers = await authService.GetTopUsers(50);
        
        if (topUsers == null)
        {
            topUsers = new List<User>();
        }
        
        var data = topUsers.Select((u, index) => new
        {
            rank = index + 1,
            username = u.Username ?? "",
            correctAnswers = u.CorrectAnswers,
            isOnline = u.LastSeen != null && u.LastSeen > DateTime.UtcNow.AddMinutes(-5),
            isCurrentUser = u.Username == currentUsername
        }).ToList();

        var response = new
        {
            users = data,
            currentUsername = currentUsername,
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Leaderboard API Error] {ex}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Server error: {ex.Message}");
    }
});

app.MapGet("/api/online-count", async context =>
{
    try
    {
        var authService = context.RequestServices.GetService<AuthService>();
        if (authService == null)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("AuthService not available");
            return;
        }

        var onlineCount = await authService.GetOnlineUserCount();
        var data = new { online = onlineCount };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(data));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Online Count API Error] {ex}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Server error: {ex.Message}");
    }
});

app.MapGet("/api/dashboard-data", async context =>
{
    try
    {
        var authService = context.RequestServices.GetService<AuthService>();
        if (authService == null)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("AuthService not available");
            return;
        }

        var allUsers = await authService.GetAllUsers();
        var onlineUsers = allUsers.Where(u => u.LastSeen != null && u.LastSeen > DateTime.UtcNow.AddMinutes(-5)).ToList();
        var cheaters = allUsers.Where(u => u.IsCheater).Count();
        var banned = allUsers.Where(u => u.IsBanned).Count();
        var topUsers = allUsers.OrderByDescending(u => u.CorrectAnswers).Take(10).ToList();
        
        var averageSuccessRate = allUsers.Where(u => u.TotalAnswered > 0)
            .Select(u => (double)u.CorrectAnswers / u.TotalAnswered)
            .DefaultIfEmpty(0).Average() * 100;

        var data = new
        {
            allUsersCount = allUsers.Count,
            onlineUsersCount = onlineUsers.Count,
            cheatersCount = cheaters,
            bannedUsersCount = banned,
            averageSuccessRate = Math.Round(averageSuccessRate, 1),
            onlineUsersList = onlineUsers.Select(u => new
            {
                username = u.Username ?? "",
                totalAnswered = u.TotalAnswered,
                correctAnswers = u.CorrectAnswers,
                successRate = u.TotalAnswered > 0 ? Math.Round((double)u.CorrectAnswers / u.TotalAnswered * 100, 0) : 0
            }).ToList(),
            topUsersList = topUsers.Select(u => new
            {
                username = u.Username ?? "",
                totalAnswered = u.TotalAnswered,
                correctAnswers = u.CorrectAnswers,
                successRate = u.TotalAnswered > 0 ? Math.Round((double)u.CorrectAnswers / u.TotalAnswered * 100, 0) : 0
            }).ToList()
        };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(data));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Dashboard API Error] {ex}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Server error: {ex.Message}");
    }
});

// 🎯 Initialize question difficulties with all images from Storage
app.MapGet("/api/init-question-difficulties", async context =>
{
    try
    {
        var storage = context.RequestServices.GetService<SupabaseStorageService>();
        var difficultyService = context.RequestServices.GetService<QuestionDifficultyService>();
        
        if (storage == null || difficultyService == null)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("Required services not available");
            return;
        }

        // Get all images from storage
        var allImages = await storage.ListFilesAsync();
        Console.WriteLine($"🎯 Found {allImages.Count} images in storage. Initializing question difficulties...");
        
        int created = 0;
        int skipped = 0;
        
        foreach (var imageName in allImages)
        {
            // Check if already exists
            var existing = await difficultyService.GetQuestionDifficulty(imageName);
            if (existing != null)
            {
                skipped++;
                continue;
            }
            
            // Create with 0 attempts (unrated) instead of a failed attempt
            bool success = await difficultyService.CreateInitialQuestion(imageName);
            if (success)
            {
                created++;
                if (created % 50 == 0)
                {
                    Console.WriteLine($"✅ Initialized {created}/{allImages.Count}...");
                }
            }
        }
        
        Console.WriteLine($"✅ Initialization complete: {created} created, {skipped} already existed");
        
        var response = new { created, skipped, total = allImages.Count };
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Init Difficulties Error] {ex}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Error: {ex.Message}");
    }
});

// 📤 Upload images to Supabase Storage (Admin only)
app.MapGet("/api/upload-images", async context =>
{
    try
    {
        var storage = context.RequestServices.GetService<SupabaseStorageService>();
        if (storage == null)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("Storage service not available");
            return;
        }

        var imagesDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "quiz_images");
        if (!System.IO.Directory.Exists(imagesDir))
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Images directory not found");
            return;
        }

        var imageFiles = System.IO.Directory.GetFiles(imagesDir)
            .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg") || f.EndsWith(".webp"))
            .OrderBy(f => f)
            .ToList();

        Console.WriteLine($"📤 Starting upload of {imageFiles.Count} images...");
        int uploaded = 0;
        int failed = 0;

        foreach (var filePath in imageFiles)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            try
            {
                using (var fileStream = System.IO.File.OpenRead(filePath))
                {
                    await storage.UploadAsync(fileStream, fileName, "image/png", overwrite: true);
                    uploaded++;
                    if (uploaded % 50 == 0)
                    {
                        Console.WriteLine($"✅ Uploaded {uploaded}/{imageFiles.Count}...");
                    }
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"❌ Failed {fileName}: {ex.Message}");
            }
        }

        Console.WriteLine($"✅ Upload complete: {uploaded} uploaded, {failed} failed");

        var response = new { uploaded, failed, total = imageFiles.Count };
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Upload Images Error] {ex}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync($"Error: {ex.Message}");
    }
});

// ✅ הדפסת הפעלה
app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = app.Urls.FirstOrDefault() ?? "http://localhost:5000";
    Console.WriteLine("❄️ WinterNET Quiz is running!");
    Console.WriteLine($"🔗 Listening on: {url}");
});

// 🧹 ניקוי קבצי סטטיסטיקה ישנים
var progressDir = Path.Combine(Directory.GetCurrentDirectory(), "progress");
if (Directory.Exists(progressDir))
{
    var files = Directory.GetFiles(progressDir, "*.json");
    var threshold = DateTime.Now.AddDays(-7);

    foreach (var file in files)
    {
        var lastWrite = File.GetLastWriteTime(file);
        if (lastWrite < threshold)
        {
            try
            {
                File.Delete(file);
                Console.WriteLine("🗑️ Deleted old session file: " + Path.GetFileName(file));
            }
            catch (Exception ex)
            {
                Console.WriteLine("⚠️ Failed to delete: " + file + " → " + ex.Message);
            }
        }
    }
}

// ✅ Render: הגדרת פורט מהסביבה
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://*:{port}");

app.Run();
