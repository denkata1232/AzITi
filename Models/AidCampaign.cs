using System.ComponentModel.DataAnnotations;

namespace NiePostigame.Models
{
    public class AidCampaign
    {
        public int Id { get; set; }

        [Required]
        public string Title { get; set; } = null!;

        public string? Description { get; set; }
        public string? LogoUrl { get; set; }
        public string? AidType { get; set; }
        public string? SimplifiedRequirement { get; set; }

        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }

        [Display(Name = "Имейл за контакт / кандидатстване")]
        public string? ContactEmail { get; set; }

        [Display(Name = "Линк към Google Форма (опционално)")]
        public string? GoogleFormUrl { get; set; }

        public ICollection<CampaignForm> CampaignForms { get; set; }
            = new List<CampaignForm>();

        public ICollection<CampaignRequiredDocument> RequiredDocuments { get; set; }
            = new List<CampaignRequiredDocument>();
    }
}