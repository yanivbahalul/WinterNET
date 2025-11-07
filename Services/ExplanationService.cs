using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Supabase;
using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;

namespace HelloWorldWeb.Services
{
    [Table("question_explanations")]
    public class QuestionExplanation : BaseModel
    {
        [PrimaryKey("QuestionFile")]
        [Column("QuestionFile")]
        public string QuestionFile { get; set; }
        
        [Column("Explanation")]
        public string Explanation { get; set; }
        
        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; }
        
        [Column("UpdatedAt")]
        public DateTime UpdatedAt { get; set; }
    }

    public class ExplanationService
    {
        private readonly Client _supabase;

        public ExplanationService(Client supabase)
        {
            _supabase = supabase;
        }

        /// <summary>
        /// Get explanation for a specific question
        /// </summary>
        public async Task<string> GetExplanation(string questionFile)
        {
            if (string.IsNullOrWhiteSpace(questionFile))
                return null;

            try
            {
                var response = await _supabase
                    .From<QuestionExplanation>()
                    .Where(x => x.QuestionFile == questionFile)
                    .Single();

                return response?.Explanation;
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
            if (questionFiles == null || !questionFiles.Any())
                return new Dictionary<string, string>();

            try
            {
                var response = await _supabase
                    .From<QuestionExplanation>()
                    .Filter("QuestionFile", Supabase.Postgrest.Constants.Operator.In, questionFiles)
                    .Get();

                var result = new Dictionary<string, string>();
                foreach (var item in response.Models)
                {
                    if (!string.IsNullOrWhiteSpace(item.QuestionFile) && !string.IsNullOrWhiteSpace(item.Explanation))
                    {
                        result[item.QuestionFile] = item.Explanation;
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
        public async Task<bool> SaveExplanation(string questionFile, string explanation)
        {
            if (string.IsNullOrWhiteSpace(questionFile))
                return false;

            try
            {
                // Check if explanation already exists
                var existing = await _supabase
                    .From<QuestionExplanation>()
                    .Where(x => x.QuestionFile == questionFile)
                    .Single();

                if (existing != null)
                {
                    // Update existing
                    existing.Explanation = explanation;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await existing.Update<QuestionExplanation>();
                }
                else
                {
                    // Insert new
                    var newExplanation = new QuestionExplanation
                    {
                        QuestionFile = questionFile,
                        Explanation = explanation,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await _supabase.From<QuestionExplanation>().Insert(newExplanation);
                }

                return true;
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
            if (string.IsNullOrWhiteSpace(questionFile))
                return false;

            try
            {
                await _supabase
                    .From<QuestionExplanation>()
                    .Where(x => x.QuestionFile == questionFile)
                    .Delete();

                return true;
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
            try
            {
                var response = await _supabase
                    .From<QuestionExplanation>()
                    .Get();

                var result = new Dictionary<string, string>();
                foreach (var item in response.Models)
                {
                    if (!string.IsNullOrWhiteSpace(item.QuestionFile) && !string.IsNullOrWhiteSpace(item.Explanation))
                    {
                        result[item.QuestionFile] = item.Explanation;
                    }
                }

                return result;
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }
    }
}

