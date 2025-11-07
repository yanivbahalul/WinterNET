using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

#nullable enable

namespace HelloWorldWeb.Services
{
    public class QuestionExplanation
    {
        public string QuestionFile { get; set; } = string.Empty;
        public string? Explanation { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ExplanationService
    {
        private readonly HttpClient _client;
        private readonly string _url;
        private readonly string _apiKey;
        
        // Cache for performance
        private Dictionary<string, string>? _explanationCache;
        private DateTime _cacheExpiry = DateTime.MinValue;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);

        public ExplanationService(IConfiguration config)
        {
            _url = config["SUPABASE_URL"]!;
            _apiKey = config["SUPABASE_KEY"]!;

            if (string.IsNullOrWhiteSpace(_url) || string.IsNullOrWhiteSpace(_apiKey))
            {
                // Don't throw - allow the app to run without explanations
                _url = string.Empty;
                _apiKey = string.Empty;
            }

            _client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                _client.DefaultRequestHeaders.Add("apikey", _apiKey);
                _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            }
        }

        private bool IsConfigured => !string.IsNullOrWhiteSpace(_url) && !string.IsNullOrWhiteSpace(_apiKey);

        /// <summary>
        /// Get explanation for a specific question
        /// </summary>
        public async Task<string?> GetExplanation(string questionFile)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(questionFile))
                return null;

            try
            {
                var endpoint = $"{_url}/rest/v1/question_explanations?QuestionFile=eq.{Uri.EscapeDataString(questionFile)}&select=*";
                var response = await _client.GetAsync(endpoint);
                
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var explanations = JsonSerializer.Deserialize<List<QuestionExplanation>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return explanations?.FirstOrDefault()?.Explanation;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Get explanations for multiple questions
        /// </summary>
        public async Task<Dictionary<string, string>> GetExplanations(List<string> questionFiles)
        {
            if (!IsConfigured || questionFiles == null || !questionFiles.Any())
                return new Dictionary<string, string>();

            try
            {
                // Build filter for multiple questions
                var filters = string.Join(",", questionFiles.Select(q => $"\"{q}\""));
                var endpoint = $"{_url}/rest/v1/question_explanations?QuestionFile=in.({filters})&select=*";
                
                var response = await _client.GetAsync(endpoint);
                
                if (!response.IsSuccessStatusCode)
                    return new Dictionary<string, string>();

                var json = await response.Content.ReadAsStringAsync();
                var explanations = JsonSerializer.Deserialize<List<QuestionExplanation>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var result = new Dictionary<string, string>();
                if (explanations != null)
                {
                    foreach (var item in explanations)
                    {
                        if (!string.IsNullOrWhiteSpace(item.QuestionFile) && !string.IsNullOrWhiteSpace(item.Explanation))
                        {
                            result[item.QuestionFile] = item.Explanation;
                        }
                    }
                }

                return result;
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Save or update explanation for a question
        /// </summary>
        public async Task<bool> SaveExplanation(string questionFile, string? explanation)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(questionFile))
                return false;

            try
            {
                // Check if exists
                var existing = await GetExplanation(questionFile);
                
                HttpResponseMessage response;
                
                if (existing != null)
                {
                    // Update
                    var updateData = new
                    {
                        Explanation = explanation ?? string.Empty
                    };
                    var json = JsonSerializer.Serialize(updateData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    content.Headers.Add("Prefer", "return=minimal");
                    
                    var endpoint = $"{_url}/rest/v1/question_explanations?QuestionFile=eq.{Uri.EscapeDataString(questionFile)}";
                    response = await _client.PatchAsync(endpoint, content);
                }
                else
                {
                    // Insert
                    var insertData = new
                    {
                        QuestionFile = questionFile,
                        Explanation = explanation ?? string.Empty,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    var json = JsonSerializer.Serialize(insertData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    content.Headers.Add("Prefer", "return=minimal");
                    
                    var endpoint = $"{_url}/rest/v1/question_explanations";
                    response = await _client.PostAsync(endpoint, content);
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Delete explanation for a question
        /// </summary>
        public async Task<bool> DeleteExplanation(string questionFile)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(questionFile))
                return false;

            try
            {
                var endpoint = $"{_url}/rest/v1/question_explanations?QuestionFile=eq.{Uri.EscapeDataString(questionFile)}";
                var response = await _client.DeleteAsync(endpoint);

                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Get all explanations with their question files
        /// </summary>
        public async Task<Dictionary<string, string>> GetAllExplanations()
        {
            if (!IsConfigured)
                return new Dictionary<string, string>();

            // Check cache
            if (_explanationCache != null && DateTime.UtcNow < _cacheExpiry)
                return new Dictionary<string, string>(_explanationCache);

            try
            {
                var endpoint = $"{_url}/rest/v1/question_explanations?select=*";
                var response = await _client.GetAsync(endpoint);
                
                if (!response.IsSuccessStatusCode)
                    return new Dictionary<string, string>();

                var json = await response.Content.ReadAsStringAsync();
                var explanations = JsonSerializer.Deserialize<List<QuestionExplanation>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var result = new Dictionary<string, string>();
                if (explanations != null)
                {
                    foreach (var item in explanations)
                    {
                        if (!string.IsNullOrWhiteSpace(item.QuestionFile) && !string.IsNullOrWhiteSpace(item.Explanation))
                        {
                            result[item.QuestionFile] = item.Explanation;
                        }
                    }
                }

                // Update cache
                _explanationCache = result;
                _cacheExpiry = DateTime.UtcNow.Add(_cacheDuration);

                return result;
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
