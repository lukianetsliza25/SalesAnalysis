// SalesAnalysis.Core/Entities/User.cs
using System.ComponentModel.DataAnnotations;

namespace SalesAnalysis.Core.Entities
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Email { get; set; }

        [Required]
        public string PasswordHash { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<SavedAnalysis> SavedAnalyses { get; set; }
    }
}