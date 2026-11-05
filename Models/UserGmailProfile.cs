
using System.ComponentModel.DataAnnotations;

namespace NiePostigame.Models
{
    public class UserGmailProfile
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string GmailAddress { get; set; } = string.Empty;

        // Криптират се автоматично чрез EF Core.
        public string? AccessToken { get; set; }

        public string? RefreshToken { get; set; }

        public DateTime? TokenExpiresAt { get; set; }

        public bool IsConnected { get; set; } = false;

        public DateTime ConnectedAt { get; set; } = DateTime.Now;
    }
}
