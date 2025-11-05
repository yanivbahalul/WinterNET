using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Supabase;

namespace HelloWorldWeb.Services
{
    public class SupabaseStorageService
    {
        private readonly Client _client;
        private readonly string _bucket;
        private readonly int _ttlSeconds;
        private bool _initialized;
        private List<string> _listCache;
        private DateTime _listCacheAt;
        private readonly TimeSpan _listTtl = TimeSpan.FromMinutes(5);
        private readonly Dictionary<string, (string url, DateTime cachedAt)> _signedUrlCache = new();
        private readonly TimeSpan _signedUrlTtl;
        private readonly string _supabaseUrl;
        private readonly string _serviceRoleKey;
        private static readonly HttpClient _httpClient = new HttpClient();

        public SupabaseStorageService(string url, string serviceRoleKey, string bucket, int ttlSeconds = 3600)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Supabase URL is required.", nameof(url));
            if (string.IsNullOrWhiteSpace(serviceRoleKey))
                throw new ArgumentException("Service Role Key is required.", nameof(serviceRoleKey));
            if (string.IsNullOrWhiteSpace(bucket))
                throw new ArgumentException("Bucket name is required.", nameof(bucket));

            _client = new Client(url, serviceRoleKey);
            _bucket = bucket;
            _ttlSeconds = ttlSeconds > 0 ? ttlSeconds : 3600;
            _signedUrlTtl = TimeSpan.FromSeconds(Math.Max(60, _ttlSeconds - 60));
            _supabaseUrl = url.TrimEnd('/');
            _serviceRoleKey = serviceRoleKey;
        }

        private async Task EnsureInitAsync()
        {
            if (_initialized) return;
            await _client.InitializeAsync();
            _initialized = true;
        }

        public async Task<string> GetSignedUrlAsync(string objectPath)
        {
            if (string.IsNullOrWhiteSpace(objectPath))
                throw new ArgumentException("objectPath is required.", nameof(objectPath));

            // Return cached if fresh
            if (_signedUrlCache.TryGetValue(objectPath, out var entry))
            {
                if (DateTime.UtcNow - entry.cachedAt < _signedUrlTtl)
                    return entry.url;
            }

            await EnsureInitAsync();
            var from = _client.Storage.From(_bucket);
            var url = await from.CreateSignedUrl(objectPath, _ttlSeconds);
            _signedUrlCache[objectPath] = (url, DateTime.UtcNow);
            return url;
        }

        public async Task<Dictionary<string, string>> GetSignedUrlsAsync(IEnumerable<string> objectPaths)
        {
            await EnsureInitAsync();
            var from = _client.Storage.From(_bucket);

            var dict = new Dictionary<string, string>();
            var tasks = new List<Task>();
            foreach (var p in objectPaths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;

                if (_signedUrlCache.TryGetValue(p, out var entry) && (DateTime.UtcNow - entry.cachedAt < _signedUrlTtl))
                {
                    dict[p] = entry.url;
                    continue;
                }

                tasks.Add(Task.Run(async () =>
                {
                    var url = await from.CreateSignedUrl(p, _ttlSeconds);
                    _signedUrlCache[p] = (url, DateTime.UtcNow);
                    lock (dict)
                    {
                        dict[p] = url;
                    }
                }));
            }
            await Task.WhenAll(tasks);
            return dict;
        }

        public async Task UploadAsync(Stream fileStream, string objectPath, string contentType = "application/octet-stream", bool overwrite = false)
        {
            if (fileStream is null) throw new ArgumentNullException(nameof(fileStream));
            if (string.IsNullOrWhiteSpace(objectPath))
                throw new ArgumentException("objectPath is required.", nameof(objectPath));

            await EnsureInitAsync();
            var from = _client.Storage.From(_bucket);

            using (var ms = new MemoryStream())
            {
                await fileStream.CopyToAsync(ms);
                var bytes = ms.ToArray();
                await from.Upload(bytes, objectPath, new Supabase.Storage.FileOptions
                {
                    Upsert = overwrite,
                    ContentType = contentType
                });
            }
        }

        public async Task DeleteAsync(string objectPath)
        {
            if (string.IsNullOrWhiteSpace(objectPath))
                throw new ArgumentException("objectPath is required.", nameof(objectPath));

            await EnsureInitAsync();
            var from = _client.Storage.From(_bucket);
            await from.Remove(objectPath);
        }

        public async Task<List<string>> ListFilesAsync(string prefix = "")
        {
            // Use simple in-memory cache to avoid listing on every request
            if (_listCache != null && (DateTime.UtcNow - _listCacheAt) < _listTtl)
                return _listCache;

            Console.WriteLine($"[Storage] Attempting to list files from bucket '{_bucket}' with prefix '{prefix}' using REST API");
            
            var list = new List<string>();

            try
            {
                // Use REST API directly instead of SDK
                var url = $"{_supabaseUrl}/storage/v1/object/list/{_bucket}";
                
                Console.WriteLine($"[Storage] REST API URL: {url}");
                Console.WriteLine($"[Storage] Bucket: {_bucket}");
                Console.WriteLine($"[Storage] Prefix: '{prefix}'");
                
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceRoleKey);
                request.Headers.Add("apikey", _serviceRoleKey);
                
                // Build request body - prefix is required!
                var requestBody = new
                {
                    limit = 1000,
                    offset = 0,
                    sortBy = new { column = "name", order = "asc" },
                    prefix = prefix ?? ""
                };
                var jsonBody = JsonSerializer.Serialize(requestBody);
                Console.WriteLine($"[Storage] Request body: {jsonBody}");
                
                request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                Console.WriteLine($"[Storage] REST API Response Status: {response.StatusCode}");
                Console.WriteLine($"[Storage] REST API Response: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}...");
                
                if (response.IsSuccessStatusCode)
                {
                    var items = JsonSerializer.Deserialize<List<StorageObject>>(responseContent, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    if (items != null)
                    {
                        Console.WriteLine($"[Storage] Found {items.Count} items");
                        foreach (var item in items)
                        {
                            if (item != null && !string.IsNullOrWhiteSpace(item.Name))
                            {
                                // Skip folders
                                if (item.Id == null && item.Name.EndsWith("/"))
                                {
                                    Console.WriteLine($"[Storage] Skipping folder: {item.Name}");
                                    continue;
                                }
                                
                                Console.WriteLine($"[Storage] Adding file: {item.Name}");
                                list.Add(item.Name);
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[Storage] REST API failed: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Storage] Error listing files with REST API: {ex.Message}");
                Console.WriteLine($"[Storage] Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine($"[Storage] Total files found: {list.Count}");
            _listCache = list;
            _listCacheAt = DateTime.UtcNow;
            return list;
        }
        
        private class StorageObject
        {
            public string Name { get; set; }
            public string Id { get; set; }
            public DateTime? Updated_at { get; set; }
            public DateTime? Created_at { get; set; }
            public DateTime? Last_accessed_at { get; set; }
            public object Metadata { get; set; }
        }

        /// <summary>
        /// Extracts the original file name from a signed URL
        /// </summary>
        /// <param name="signedUrl">The signed URL from Supabase Storage</param>
        /// <returns>The original file name or null if extraction fails</returns>
        public static string ExtractFileNameFromSignedUrl(string signedUrl)
        {
            if (string.IsNullOrWhiteSpace(signedUrl))
                return null;

            try
            {
                // Parse the JWT token from the URL
                var uri = new Uri(signedUrl);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var token = query["token"];
                
                if (string.IsNullOrWhiteSpace(token))
                    return null;

                // Decode the JWT token (we only need the payload)
                var parts = token.Split('.');
                if (parts.Length != 3)
                    return null;

                var payload = parts[1];
                // Add padding if needed
                payload = payload.PadRight(4 * ((payload.Length + 3) / 4), '=');
                
                // Decode base64url to base64
                payload = payload.Replace('-', '+').Replace('_', '/');
                
                var jsonBytes = Convert.FromBase64String(payload);
                var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
                
                // Parse the JSON to get the "url" field
                var tokenData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (tokenData.TryGetValue("url", out var urlObj))
                {
                    var url = urlObj.ToString();
                    // Extract the file name from the URL
                    var fileName = System.IO.Path.GetFileName(url);
                    return fileName;
                }
            }
            catch (Exception)
            {
                // If extraction fails, return null
            }
            
            return null;
        }
    }
}

