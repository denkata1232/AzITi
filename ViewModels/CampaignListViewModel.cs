namespace NiePostigame.ViewModels
{
    public class CampaignListViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        
        // Добавете това свойство:
        public string? ContactEmail { get; set; }

        public string SimplifiedRequirement { get; set; } = string.Empty;
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public List<Models.CampaignForm> CampaignForms { get; set; } = new();
        public bool IsActive { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }
}