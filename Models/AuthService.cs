using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Supabase;
using Supabase.Postgrest.Models;
using Microsoft.Extensions.Configuration;

#nullable enable

namespace HelloWorldWeb.Models
{
    public class User : BaseModel
    {
        public User() : base() { }
        
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public int CorrectAnswers { get; set; }
        public int TotalAnswered { get; set; }
        public bool IsCheater { get; set; }
        public bool IsBanned { get; set; }
        public DateTime? LastSeen { get; set; }
    }

    public class AuthService
    {
        private readonly Client? _supabase;
        private readonly IConfiguration _configuration;
        private const string TABLE_NAME = "WinterUsers";

        public AuthService(IConfiguration configuration)
        {
            _configuration = configuration;
            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? configuration["Supabase:Url"];
            var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY") ?? configuration["Supabase:Key"];

            if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseKey))
            {
                throw new Exception("Missing Supabase ENV vars.");
            }

            var options = new SupabaseOptions
            {
                AutoConnectRealtime = true
            };

            _supabase = new Client(supabaseUrl, supabaseKey, options);
        }

        public async Task<User?> Authenticate(string username, string password)
        {
            if (_supabase == null) return null;

            try
            {
                var response = await _supabase
                    .From<User>()
                    .Where(x => x.Username == username)
                    .Where(x => x.Password == password)
                    .Single();

                return response;
            }
            catch
            {
                return null;
            }
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

            var newUser = new User
            {
                Username = username,
                Password = password,
                CorrectAnswers = 0,
                TotalAnswered = 0,
                IsCheater = false,
                IsBanned = false,
                LastSeen = DateTime.UtcNow
            };

            if (_supabase == null) return false;

            try
            {
                await _supabase.From<User>().Insert(newUser);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Register] Error: {ex.Message}");
                return false;
            }
        }

        public async Task<User?> GetUser(string username)
        {
            if (_supabase == null) return null;

            try
            {
                var response = await _supabase
                    .From<User>()
                    .Where(x => x.Username == username)
                    .Single();

                return response;
            }
            catch
            {
                return null;
            }
        }

        public async Task UpdateUser(User updatedUser)
        {
            if (_supabase == null) return;

            try
            {
                updatedUser.LastSeen = DateTime.UtcNow;
                await _supabase.From<User>().Update(updatedUser);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateUser] Error: {ex.Message}");
            }
        }

        public async Task<List<User>> GetAllUsers()
        {
            if (_supabase == null) return new List<User>();

            try
            {
                var response = await _supabase.From<User>().Get();
                return response.Models;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetAllUsers] Error: {ex.Message}");
                return new List<User>();
            }
        }

        public async Task<bool> DeleteUser(string username)
        {
            if (_supabase == null) return false;

            try
            {
                await _supabase.From<User>()
                    .Where(x => x.Username == username)
                    .Delete();
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
            if (_supabase == null) return false;

            try
            {
                // נסיון לקרוא מהטבלה כדי לבדוק חיבור
                await _supabase.From<User>().Get();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
