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
using Microsoft.Extensions.Configuration;


#nullable enable

namespace HelloWorldWeb.Models
{
    [Table("WinterUsers")]
    public class User : BaseModel
    {
        public User() : base() { }
        
        [PrimaryKey("Username")]
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

            Console.WriteLine($"[AuthService] Supabase URL: {(string.IsNullOrEmpty(supabaseUrl) ? "MISSING" : "OK")}");
            Console.WriteLine($"[AuthService] Supabase Key: {(string.IsNullOrEmpty(supabaseKey) ? "MISSING" : "OK")}");

            if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(supabaseKey))
            {
                var options = new SupabaseOptions
                {
                    AutoConnectRealtime = true
                };

                _supabase = new Client(supabaseUrl, supabaseKey, options);
                Console.WriteLine("[AuthService] Supabase client initialized successfully");
            }
            else
            {
                Console.WriteLine("[AuthService] WARNING: No Supabase credentials found. Using fallback to local storage.");
            }
        }

        public async Task<User?> Authenticate(string username, string password)
        {
            // Try Supabase first
            if (_supabase != null)
            {
                try
                {
                    Console.WriteLine($"[Authenticate] Attempting Supabase authentication: {username}");
                    var response = await _supabase
                        .From<User>()
                        .Where(x => x.Username == username)
                        .Where(x => x.Password == password)
                        .Single();

                    Console.WriteLine($"[Authenticate] Successfully authenticated via Supabase: {username}");
                    return response;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Authenticate] Supabase error: {ex.Message}");
                }
            }
            
            // Fallback to local storage
            Console.WriteLine($"[Authenticate] Using local storage fallback: {username}");
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
            // Try Supabase first
            if (_supabase != null)
            {
                try
                {
                    Console.WriteLine($"[UpdateUser] Updating user via Supabase: {updatedUser.Username}");
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
                    
                    Console.WriteLine($"[UpdateUser] Successfully updated user: {updatedUser.Username}");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UpdateUser] Supabase error: {ex.Message}");
                }
            }
            
            // Fallback to local storage
            Console.WriteLine($"[UpdateUser] Using local storage fallback for: {updatedUser.Username}");
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
                Console.WriteLine($"[UpdateUser] Local storage error: {ex.Message}");
            }
        }

        public async Task<List<User>> GetAllUsers()
        {
            // Try Supabase first
            if (_supabase != null)
            {
                try
                {
                    Console.WriteLine("[GetAllUsers] Fetching users from Supabase");
                    var response = await _supabase.From<User>().Get();
                    Console.WriteLine($"[GetAllUsers] Retrieved {response.Models.Count} users from Supabase");
                    return response.Models;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GetAllUsers] Supabase error: {ex.Message}");
                }
            }
            
            // Fallback to local storage
            Console.WriteLine("[GetAllUsers] Using local storage fallback");
            return await GetAllUsersLocal();
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
                Console.WriteLine($"[GetAllUsersLocal] Error: {ex.Message}");
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
                Console.WriteLine($"[SaveUsersLocal] Error: {ex.Message}");
            }
        }
    }
}
