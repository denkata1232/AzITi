using System.ComponentModel.DataAnnotations;

namespace NiePostigame.Models
{
    public class CampaignRequiredDocument
    {
        public int Id { get; set; }

        public int AidCampaignId { get; set; }

        public AidCampaign AidCampaign { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string DocumentType { get; set; } = string.Empty;
    }
}