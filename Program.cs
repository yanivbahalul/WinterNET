using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using HelloWorldWeb.Models;

var builder = WebApplication.CreateBuilder(args);

// Razor Pages + Session
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor(); 
builder.Services.AddSingleton<AuthService>();
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
