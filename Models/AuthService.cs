using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

#nullable enable

namespace HelloWorldWeb.Models
{
    public class AuthService
    {
        private readonly string _usersFilePath;

        public AuthService()
        {
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
            if (!Directory.Exists(dataDir))
                Directory.CreateDirectory(dataDir);
            
            _usersFilePath = Path.Combine(dataDir, "users.json");
        }

        public async Task<User?> Authenticate(string username, string password)
        {
            var users = await GetAllUsers();
            return users.FirstOrDefault(u => u.Username == username && u.Password == password);
        }

        public async Task<bool> Register(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return false;

            if (username.Length < 5 || password.Length < 5)
                return false;

            if (!Regex.IsMatch(username, "^[a-zA-Z0-9א-ת]+$") || !Regex.IsMatch(password, "^[a-zA-Z0-9א-ת]+$"))
                return false;

            var existingUser = await GetUser(username);
            if (existingUser != null)
                return false;

            var users = await GetAllUsers();
            users.Add(new User
            {
                Username = username,
                Password = password,
                CorrectAnswers = 0,
                TotalAnswered = 0,
                IsCheater = false,
                IsBanned = false,
                LastSeen = DateTime.UtcNow
            });

            await SaveUsers(users);
            return true;
        }

        public async Task<User?> GetUser(string username)
        {
            var users = await GetAllUsers();
            return users.FirstOrDefault(u => u.Username == username);
        }

        public async Task UpdateUser(User updatedUser)
        {
            var users = await GetAllUsers();
            var index = users.FindIndex(u => u.Username == updatedUser.Username);
            
            if (index >= 0)
            {
                users[index] = updatedUser;
                await SaveUsers(users);
            }
        }

        public async Task<List<User>> GetAllUsers()
        {
            try
            {
                if (File.Exists(_usersFilePath))
                {
                    var json = await File.ReadAllTextAsync(_usersFilePath);
                    return JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
                }
                return new List<User>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetAllUsers] Error: {ex.Message}");
                return new List<User>();
            }
        }

        private async Task SaveUsers(List<User> users)
        {
            var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_usersFilePath, json);
        }

        public async Task<bool> DeleteUser(string username)
        {
            try
            {
                var users = await GetAllUsers();
                users.RemoveAll(u => u.Username == username);
                await SaveUsers(users);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DeleteUser] Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CheckConnection()
        {
            return true; // תמיד עובד עם סטורג׳ מקומי
        }
    }
}
