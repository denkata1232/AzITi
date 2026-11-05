
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using NiePostigame.Data;
using NiePostigame.Models;
using NiePostigame.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NiePostigame.Controllers
{
    [Authorize]
    public class ApplicationController : Controller
    {
        private const string GmailSendUrl =
            "https://gmail.googleapis.com/upload/gmail/v1/users/me/messages/send?uploadType=media";

        private const long MaxAttachmentsBytes =
            15 * 1024 * 1024;

        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly SecureDocumentService _documents;
        private readonly CampaignDocumentService _documentService;

        public ApplicationController(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            SecureDocumentService documents,
            CampaignDocumentService documentService)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _documents = documents;
            _documentService = documentService;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(int campaignId)
        {
            var email = User.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] =
                    "Моля, влезте отново с Google.";

                return RedirectToAction("Login", "Account");
            }

            var detailsRedirect = RedirectToAction(
                "Details", "AidCampaigns",
                new { id = campaignId });

            var existingApplication =
                await _context.UserApplications
                    .AnyAsync(a =>
                        a.UserId == email &&
                        a.CampaignId == campaignId);

            if (existingApplication)
            {
                TempData["ErrorMessage"] =
                    "Вече сте кандидатствали за тази кампания.";

                return detailsRedirect;
            }

            var missingDocuments =
                await _documentService.GetMissingDocumentsAsync(
                campaignId, email);

            if (missingDocuments.Any())
            {
                TempData["ErrorMessage"] =
                    "Не можете да кандидатствате. " +
                    "Липсват задължителни документи: " +
                    string.Join(", ", missingDocuments) + ".";

                return detailsRedirect;
            }

            var campaign = await _context.AidCampaigns
                .FindAsync(campaignId);

            if (campaign == null ||
                string.IsNullOrWhiteSpace(campaign.ContactEmail))
            {
                TempData["ErrorMessage"] =
                    "Кампанията няма валиден имейл за контакт.";

                return detailsRedirect;
            }

            var profile = await _context.UserProfiles
                .Include(p => p.Documents)
                .FirstOrDefaultAsync(p => p.UserId == email);

            if (profile == null ||
                string.IsNullOrWhiteSpace(profile.FirstName) ||
                string.IsNullOrWhiteSpace(profile.Egn))
            {
                TempData["ErrorMessage"] =
                    "Попълнете имената и ЕГН в профила си.";

                return RedirectToAction("Index", "Profile");
            }

            var gmailProfile = await _context.UserGmailProfiles
                .FirstOrDefaultAsync(p => p.GmailAddress == email);

            var refreshToken = gmailProfile?.RefreshToken;

            // Приоритет на валидния токен от базата.
            var accessToken = gmailProfile?.AccessToken;

            if (gmailProfile?.TokenExpiresAt is DateTime expires &&
                expires <= DateTime.UtcNow.AddMinutes(2))
            {
                accessToken = null;
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                accessToken = HttpContext.Session.GetString(
                    "GoogleAccessToken");
            }

            if (string.IsNullOrWhiteSpace(accessToken) &&
                !string.IsNullOrWhiteSpace(refreshToken))
            {
                accessToken = await RefreshGoogleAccessTokenAsync(
                    refreshToken, email);
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                TempData["ErrorMessage"] =
                    "Няма активен Google токен. Влезте отново.";

                return detailsRedirect;
            }

            try
            {
                var message = new MimeMessage();

                message.From.Add(
                    MailboxAddress.Parse(email));

                message.To.Add(
                    MailboxAddress.Parse(campaign.ContactEmail));

                message.Subject =
                    $"Заявление за подкрепа: {campaign.Title} - " +
                    $"{profile.FirstName} {profile.LastName}";

                var body = new StringBuilder();

                body.AppendLine("Здравейте,");
                body.AppendLine();
                body.AppendLine(
                    $"Кандидатствам за кампания: {campaign.Title}");
                body.AppendLine();
                body.AppendLine("--- ДЕКЛАРАЦИЯ НА КАНДИДАТА ---");
                body.AppendLine(
                    $"Имена: {profile.FirstName} " +
                    $"{profile.MiddleName} {profile.LastName}");
                body.AppendLine($"ЕГН: {profile.Egn}");
                body.AppendLine($"Имейл за връзка: {email}");
                body.AppendLine(
                    "Декларирам, че предоставените данни " +
                    "и приложените документи са верни.");
                body.AppendLine();
                body.AppendLine("С уважение,");
                body.AppendLine(
                    $"{profile.FirstName} {profile.LastName}");

                var mimeBuilder = new BodyBuilder
                {
                    TextBody = body.ToString()
                };

                long totalSize = 0;

                foreach (var document in profile.Documents)
                {
                    if (string.IsNullOrWhiteSpace(
                        document.FilePath))
                    {
                        continue;
                    }

                    // Декриптиране само за прикачване.
                    var bytes = await _documents.ReadAsync(
                        document,
                        HttpContext.RequestAborted);

                    totalSize += bytes.LongLength;

                    if (totalSize > MaxAttachmentsBytes)
                    {
                        TempData["ErrorMessage"] =
                            "Документите надвишават общия лимит от 15 MB.";

                        return detailsRedirect;
                    }

                    var filename = Path.GetFileName(
                        document.OriginalFileName ?? "document.pdf");

                    mimeBuilder.Attachments.Add(
                        filename, bytes);
                }

                message.Body = mimeBuilder.ToMessageBody();

                using var response = await SendGmailMessageAsync(
                    message, accessToken);

                HttpStatusCode statusCode = response.StatusCode;

                if (statusCode == HttpStatusCode.Unauthorized &&
                    !string.IsNullOrWhiteSpace(refreshToken))
                {
                    var refreshedToken =
                        await RefreshGoogleAccessTokenAsync(
                            refreshToken, email);

                    if (!string.IsNullOrWhiteSpace(refreshedToken))
                    {
                        using var retryResponse =
                            await SendGmailMessageAsync(
                                message, refreshedToken);

                        statusCode = retryResponse.StatusCode;
                    }
                }

                if ((int)statusCode >= 200 &&
                    (int)statusCode < 300)
                {
                    _context.UserApplications.Add(
                        new UserApplication
                        {
                            UserId = email,
                            CampaignId = campaignId,
                            AppliedAt = DateTime.UtcNow,
                            Status = "Успешно изпратено"
                        });

                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] =
                        "Заявлението и документите са изпратени успешно.";
                }
                else if (statusCode ==
                         HttpStatusCode.Unauthorized)
                {
                    HttpContext.Session.Remove(
                        "GoogleAccessToken");

                    TempData["ErrorMessage"] =
                        "Google токенът е изтекъл. Влезте отново.";
                }
                else
                {
                    TempData["ErrorMessage"] =
                        $"Грешка от Google (код {(int)statusCode}).";
                }
            }
            catch (FileNotFoundException)
            {
                TempData["ErrorMessage"] =
                    "Един от документите липсва. Качете го отново.";
            }
            catch (CryptographicException)
            {
                TempData["ErrorMessage"] =
                    "Един от документите не може да бъде декриптиран.";
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] =
                    "Възникна грешка при изпращането на заявлението.";
            }

            return detailsRedirect;
        }

        private async Task<HttpResponseMessage> SendGmailMessageAsync(
            MimeMessage message,
            string accessToken)
        {
            using var stream = new MemoryStream();

            await message.WriteToAsync(stream);
            stream.Position = 0;

            using var request = new HttpRequestMessage(
                HttpMethod.Post, GmailSendUrl);

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer", accessToken);

            request.Content = new StreamContent(stream);
            request.Content.Headers.ContentType =
                new MediaTypeHeaderValue("message/rfc822");

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(90);

            return await client.SendAsync(request);
        }

        private async Task<string?> RefreshGoogleAccessTokenAsync(
            string refreshToken,
            string userEmail)
        {
            var client = _httpClientFactory.CreateClient();

            using var content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] =
                        _configuration[
                            "Authentication:Google:ClientId"] ?? "",

                    ["client_secret"] =
                        _configuration[
                            "Authentication:Google:ClientSecret"] ?? "",

                    ["refresh_token"] = refreshToken,
                    ["grant_type"] = "refresh_token"
                });

            using var response = await client.PostAsync(
                "https://oauth2.googleapis.com/token",
                content);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content
                .ReadFromJsonAsync<JsonElement>();

            if (!json.TryGetProperty(
                    "access_token", out var tokenProperty))
            {
                return null;
            }

            var newToken = tokenProperty.GetString();

            if (string.IsNullOrWhiteSpace(newToken))
                return null;

            HttpContext.Session.SetString(
                "GoogleAccessToken", newToken);

            var profile = await _context.UserGmailProfiles
                .FirstOrDefaultAsync(p =>
                    p.GmailAddress == userEmail);

            if (profile != null)
            {
                profile.AccessToken = newToken;

                int expiresIn = 3600;

                if (json.TryGetProperty(
                        "expires_in", out var expiresProperty) &&
                    expiresProperty.TryGetInt32(out var seconds))
                {
                    expiresIn = seconds;
                }

                profile.TokenExpiresAt =
                    DateTime.UtcNow.AddSeconds(expiresIn);

                await _context.SaveChangesAsync();
            }

            return newToken;
        }
    }
}
