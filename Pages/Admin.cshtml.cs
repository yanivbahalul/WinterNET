using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using HelloWorldWeb.Models;
using HelloWorldWeb.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HelloWorldWeb.Pages
{
    public class AdminModel : PageModel
    {
        private readonly AuthService _authService;
        private readonly QuestionDifficultyService _difficultyService;

        public AdminModel(AuthService authService, QuestionDifficultyService difficultyService = null)
        {
            _authService = authService;
            _difficultyService = difficultyService;
        }

        public List<User> AllUsers { get; set; } = new();
        public List<User> Cheaters { get; set; } = new();
        public List<User> BannedUsers { get; set; } = new();
        public List<User> OnlineUsers { get; set; } = new();
        public List<User> TopUsers { get; set; } = new();
        public double AverageSuccessRate { get; set; }
        
        public List<QuestionDifficulty> DifficultyQuestions { get; set; } = new();
        public int EasyCount { get; set; }
        public int MediumCount { get; set; }
        public int HardCount { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                await LoadData();
                await LoadDifficultyData();
                return Page();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }

        private async Task LoadDifficultyData()
        {
            try
            {
                if (_difficultyService != null)
                {
                    // Auto-recalculate difficulties to ensure they're always up-to-date
                    await _difficultyService.RecalculateAllDifficulties();
                    
                    // Load ALL questions (no limit)
                    DifficultyQuestions = await _difficultyService.GetAllQuestions(1000);
                    EasyCount = DifficultyQuestions.Count(q => q.Difficulty == "easy");
                    MediumCount = DifficultyQuestions.Count(q => q.Difficulty == "medium");
                    HardCount = DifficultyQuestions.Count(q => q.Difficulty == "hard");
                }
            }
            catch (Exception ex)
            {
                DifficultyQuestions = new List<QuestionDifficulty>();
            }
        }

        private async Task LoadData()
        {
            try
            {
                AllUsers = await _authService.GetAllUsers();
                Cheaters = AllUsers.Where(u => u.IsCheater).ToList();
                BannedUsers = AllUsers.Where(u => u.IsBanned).ToList();
                OnlineUsers = AllUsers.Where(u => u.LastSeen != null && u.LastSeen > DateTime.UtcNow.AddMinutes(-5)).ToList();
                TopUsers = AllUsers.OrderByDescending(u => u.CorrectAnswers).Take(5).ToList();
                // Calculate average success rate, excluding users with 0% success rate
                AverageSuccessRate = AllUsers
                    .Where(u => u.TotalAnswered > 0 && u.CorrectAnswers > 0)
                    .Select(u => (double)u.CorrectAnswers / u.TotalAnswered)
                    .DefaultIfEmpty(0).Average() * 100;
            }
            catch (Exception ex)
            {
                Cheaters = new List<User>();
                BannedUsers = new List<User>();
                TopUsers = new List<User>();
                OnlineUsers = new List<User>();
                AllUsers = new List<User>();
                AverageSuccessRate = 0;
            }
        }
    }
}
