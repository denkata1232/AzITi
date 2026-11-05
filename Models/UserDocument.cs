using System.ComponentModel.DataAnnotations;

namespace NiePostigame.Models
{
    public class UserDocument
    {
        public int Id { get; set; }

        public int UserProfileId { get; set; }
        public UserProfile? UserProfile { get; set; }

        [Required]
        [Display(Name = "Тип документ")]
        public string DocumentType { get; set; } = string.Empty; // Смъртен акт, ТЕЛК, Уверение за студент, Уверение за ученик

        [Display(Name = "Път до файл")]
        public string? FilePath { get; set; }

        [Display(Name = "Оригинално име на файл")]
        public string? OriginalFileName { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.Now;
    }
}