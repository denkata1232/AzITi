namespace NiePostigame.Models
{
    public class CampaignForm
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string FormUrl { get; set; } = null!;
        
        public int AidCampaignId { get; set; }
        public AidCampaign AidCampaign { get; set; } = null!;
    }
}