
using System.ComponentModel.DataAnnotations;

namespace NiePostigame.Models
{
    public class UserProfile
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Display(Name = "Име")]
        public string? FirstName { get; set; }

        [Display(Name = "Презиме")]
        public string? MiddleName { get; set; }

        [Display(Name = "Фамилия")]
        public string? LastName { get; set; }

        [Display(Name = "ЕГН")]
        [StringLength(
            10,
            MinimumLength = 10,
            ErrorMessage = "ЕГН трябва да съдържа точно 10 символа."
        )]
        public string? Egn { get; set; }

        [Display(Name = "IBAN")]
        [StringLength(34, ErrorMessage = "IBAN не може да надвишава 34 символа.")]
        public string? Iban { get; set; }

        [Display(Name = "Телефонен номер")]
        [Phone(ErrorMessage = "Моля, въведете валиден телефонен номер.")]
        public string? PhoneNumber { get; set; }

        // Прикачени документи
        public List<UserDocument> Documents { get; set; } = new();
    }
}
