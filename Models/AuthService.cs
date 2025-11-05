using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Supabase;
using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Converters;
using Microsoft.Extensions.Configuration;


#nullable enable

namespace HelloWorldWeb.Models
{
    [Table("WinterUsers")]
    public class User : BaseModel
    {
        public User() : base() { }
        
        [PrimaryKey("Username")]
        [Column("Username")]
        public string Username { get; set; } = "";
        
        [Column("Password")]
        public string Password { get; set; } = "";
        
        [Column("CorrectAnswers")]
        public int CorrectAnswers { get; set; }
        
        [Column("TotalAnswered")]
        public int TotalAnswered { get; set; }
        
        [Column("IsCheater")]
        public bool IsCheater { get; set; }
        
        [Column("IsBanned")]
        public bool IsBanned { get; set; }
        
        [Column("LastSeen")]
        public DateTime? LastSeen { get; set; }
    }

    public class AuthService
    {
        private readonly Client? _supabase;
        private readonly IConfiguration _configuration;
        private readonly string _usersFilePath;
        private const string TABLE_NAME = "WinterUsers";

        public AuthService(IConfiguration configuration)
        {
            _configuration = configuration;
            
            // Setup local storage fallback
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
            if (!Directory.Exists(dataDir))
                Directory.CreateDirectory(dataDir);
            _usersFilePath = Path.Combine(dataDir, "users.json");
            
            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? configuration["Supabase:Url"];
            var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY") ?? configuration["Supabase:Key"];

            if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(supabaseKey))
            {
                var options = new SupabaseOptions
                {
                    AutoConnectRealtime = true
                };

                _supabase = new Client(supabaseUrl, supabaseKey, options);
            }
            else
            {
            }
        }

        public async Task<User?> Authenticate(string username, string password)
        {
            // Try Supabase first
            if (_supabase != null)
            {
                try
                {
                    var response = await _supabase
                        .From<User>()
                        .Where(x => x.Username == username)
                        .Where(x => x.Password == password)
                        .Single();

                    return response;
                }
                catch (Exception ex)
                {
                }
            }
            
            // Fallback to local storage
            var users = await GetAllUsersLocal();
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
            // Try Supabase first
            if (_supabase != null)
            {
                try
                {
                    updatedUser.LastSeen = DateTime.UtcNow;
                    
                    // Update using direct property updates
                    // Supabase Postgrest .Update() requires an object with PrimaryKey set
                    // Since Username is our PrimaryKey, we need to set it explicitly
                    
                    var userToUpdate = new User
                    {
                        Username = updatedUser.Username,  // Primary Key must be set
                        Password = updatedUser.Password,
                        CorrectAnswers = updatedUser.CorrectAnswers,
                        TotalAnswered = updatedUser.TotalAnswered,
                        IsCheater = updatedUser.IsCheater,
                        IsBanned = updatedUser.IsBanned,
                        LastSeen = updatedUser.LastSeen
                    };
                    
                    // Update in Supabase
                    await _supabase.From<User>().Update(userToUpdate);
                    
                    return;
                }
                catch (Exception ex)
                {
                }
            }
            
            // Fallback to local storage
            try
            {
                var users = await GetAllUsersLocal();
                var index = users.FindIndex(u => u.Username == updatedUser.Username);
                if (index >= 0)
                {
                    users[index] = updatedUser;
                    await SaveUsersLocal(users);
                }
            }
            catch (Exception ex)
            {
            }
        }

        public async Task<List<User>> GetTopUsers(int count)
        {
            // Optimized query: Get only top users sorted by CorrectAnswers
            if (_supabase != null)
            {
                try
                {
                    // Use Supabase's built-in ordering and limiting for better performance
                    var response = await _supabase
                        .From<User>()
                        .Order("CorrectAnswers", Supabase.Postgrest.Constants.Ordering.Descending)
                        .Order("TotalAnswered", Supabase.Postgrest.Constants.Ordering.Ascending)
                        .Limit(count)
                        .Get();
                    
                    return response.Models.ToList();
                }
                catch (Exception ex)
                {
                    // Fallback: if the optimized query fails, use the old approach
                    try
                    {
                        var response = await _supabase.From<User>().Get();
                        var sortedUsers = response.Models
                            .OrderByDescending(u => u.CorrectAnswers)
                            .ThenBy(u => u.TotalAnswered)
                            .Take(count)
                            .ToList();
                        return sortedUsers;
                    }
                    catch (Exception ex2)
                    {
                    }
                }
            }
            
            // Fallback to local storage
            var allUsers = await GetAllUsersLocal();
            return allUsers
                .OrderByDescending(u => u.CorrectAnswers)
                .ThenBy(u => u.TotalAnswered)
                .Take(count)
                .ToList();
        }

        public async Task<List<User>> GetAllUsers(int? limit = null, List<string>? columns = null)
        {
            // Try Supabase first
            if (_supabase != null)
            {
                try
                {
                    var response = await _supabase.From<User>().Get();
                    
                    // Apply limit and column filtering in memory
                    var users = response.Models;
                    
                    if (limit.HasValue && limit.Value > 0)
                    {
                        users = users.Take(limit.Value).ToList();
                    }
                    
                    return users;
                }
                catch (Exception ex)
                {
                }
            }
            
            // Fallback to local storage
            var allUsers = await GetAllUsersLocal();
            
            // Apply limit in local storage
            if (limit.HasValue && limit.Value > 0)
            {
                return allUsers.Take(limit.Value).ToList();
            }
            
            return allUsers;
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
                return false;
            }
        }

        public async Task<bool> CheckConnection()
        {
            if (_supabase != null)
            {
                try
                {
                    await _supabase.From<User>().Get();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return true; // Local storage always works
        }

        // Local storage fallback methods
        private async Task<List<User>> GetAllUsersLocal()
        {
            try
            {
                if (File.Exists(_usersFilePath))
                {
                    var json = await File.ReadAllTextAsync(_usersFilePath);
                    var users = JsonSerializer.Deserialize<List<User>>(json);
                    return users ?? new List<User>();
                }
                return new List<User>();
            }
            catch (Exception ex)
            {
                return new List<User>();
            }
        }

        private async Task SaveUsersLocal(List<User> users)
        {
            try
            {
                var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_usersFilePath, json);
            }
            catch (Exception ex)
            {
            }
        }

        public async Task<int> GetOnlineUserCount()
        {
            try
            {
                var users = await GetAllUsers();
                var count = users.Count(u => u.LastSeen != null && u.LastSeen > DateTime.UtcNow.AddMinutes(-5));
                return count;
            }
            catch (Exception ex)
            {
                return 0;
            }
        }
    }
}
