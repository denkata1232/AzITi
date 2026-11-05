using Microsoft.EntityFrameworkCore;
using NiePostigame.Data;

namespace NiePostigame.Services
{
    public class CampaignDocumentService
    {
        private readonly ApplicationDbContext _context;

        public CampaignDocumentService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<string>> GetMissingDocumentsAsync(
            int campaignId,
            string userEmail)
        {
            var required = await _context.CampaignRequiredDocuments
                .AsNoTracking()
                .Where(x => x.AidCampaignId == campaignId)
                .Select(x => x.DocumentType)
                .ToListAsync();

            if (required.Count == 0)
                return new List<string>();

            var uploaded = await _context.UserDocuments
                .AsNoTracking()
                .Where(d =>
                    d.UserProfile != null &&
                    d.UserProfile.UserId == userEmail &&
                    d.FilePath != null &&
                    d.FilePath != "")
                .Select(d => d.DocumentType)
                .ToListAsync();

            var uploadedTypes = new HashSet<string>(
                uploaded.Select(x => x.Trim()),
                StringComparer.OrdinalIgnoreCase);

            return required
                .Where(x => !uploadedTypes.Contains(x.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}