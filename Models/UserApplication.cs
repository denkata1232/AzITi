using System.ComponentModel.DataAnnotations;

namespace NiePostigame.Models
{
    public class UserApplication
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty; // Имейл на потребителя

        public int CampaignId { get; set; }
        public AidCampaign? Campaign { get; set; }

        public DateTime AppliedAt { get; set; } = DateTime.Now;
        public string Status { get; set; } = "Изпратено";
    }
}