
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NiePostigame.Data;
using NiePostigame.Models;
using NiePostigame.Services;
using System.Security.Claims;
using System.Security.Cryptography;

namespace NiePostigame.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SecureDocumentService _documents;

        public ProfileController(
            ApplicationDbContext context,
            SecureDocumentService documents)
        {
            _context = context;
            _documents = documents;
        }

        private string? CurrentEmail =>
            User.FindFirstValue(ClaimTypes.Email);

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var email = CurrentEmail;

            if (string.IsNullOrWhiteSpace(email))
                return Challenge();

            var profile = await _context.UserProfiles
                .Include(p => p.Documents)
                .FirstOrDefaultAsync(p => p.UserId == email);

            if (profile == null)
            {
                profile = new UserProfile { UserId = email };
                _context.UserProfiles.Add(profile);
                await _context.SaveChangesAsync();
            }

            return View(profile);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<IActionResult> Update(
            UserProfile model,
            string? newDocumentType,
            IFormFile? documentFile)
        {
            var email = CurrentEmail;

            if (string.IsNullOrWhiteSpace(email))
                return Challenge();

            var profile = await _context.UserProfiles
                .Include(p => p.Documents)
                .FirstOrDefaultAsync(p => p.UserId == email);

            if (profile == null)
                return NotFound();

            var egn = model.Egn?.Trim();

            if (!string.IsNullOrEmpty(egn) &&
                (egn.Length != 10 ||
                 !egn.All(c => c >= '0' && c <= '9')))
            {
                TempData["ErrorMessage"] =
                    "ЕГН трябва да съдържа точно 10 цифри.";
                return RedirectToAction(nameof(Index));
            }

            if (documentFile != null &&
                string.IsNullOrWhiteSpace(newDocumentType))
            {
                TempData["ErrorMessage"] =
                    "Моля, изберете вид на документа.";
                return RedirectToAction(nameof(Index));
            }

            if (!string.IsNullOrWhiteSpace(newDocumentType) &&
                (documentFile == null || documentFile.Length == 0))
            {
                TempData["ErrorMessage"] =
                    "Моля, изберете файл за качване.";
                return RedirectToAction(nameof(Index));
            }

            if (newDocumentType?.Length > 100)
            {
                TempData["ErrorMessage"] =
                    "Типът на документа е прекалено дълъг.";
                return RedirectToAction(nameof(Index));
            }

            if (documentFile != null &&
                Path.GetFileName(documentFile.FileName).Length > 255)
            {
                TempData["ErrorMessage"] =
                    "Името на файла е прекалено дълго.";
                return RedirectToAction(nameof(Index));
            }

            string? savedPath = null;

            try
            {
                if (documentFile != null)
                {
                    savedPath = await _documents.SaveAsync(
                        documentFile,
                        HttpContext.RequestAborted);

                    profile.Documents.Add(new UserDocument
                    {
                        DocumentType = newDocumentType!.Trim(),
                        FilePath = savedPath,
                        OriginalFileName = Path.GetFileName(
                            documentFile.FileName)
                    });
                }

                profile.FirstName = model.FirstName?.Trim();
                profile.MiddleName = model.MiddleName?.Trim();
                profile.LastName = model.LastName?.Trim();
                profile.Egn = egn;
                profile.Iban = model.Iban?.Trim().Replace(" ", "").ToUpperInvariant();
                profile.PhoneNumber = model.PhoneNumber?.Trim();

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] =
                    "Профилът и документите са обновени успешно!";
            }
            catch (InvalidOperationException)
            {
                if (savedPath != null)
                    TryDeleteNewFile(savedPath);

                TempData["ErrorMessage"] =
                    "Невалиден документ. Позволени са PDF, JPG и PNG до 10 MB.";
            }
            catch (Exception)
            {
                if (savedPath != null)
                    TryDeleteNewFile(savedPath);

                TempData["ErrorMessage"] =
                    "Възникна грешка при запазването на профила.";
            }

            return RedirectToAction(nameof(Index));
        }

        private void TryDeleteNewFile(string path)
        {
            try
            {
                _documents.Delete(
                    new UserDocument { FilePath = path });
            }
            catch (IOException)
            {
                // Файлът се почиства административно.
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadDocument(int id)
        {
            var email = CurrentEmail;

            if (string.IsNullOrWhiteSpace(email))
                return Challenge();

            var document = await _context.UserDocuments
                .Include(d => d.UserProfile)
                .FirstOrDefaultAsync(d =>
                    d.Id == id &&
                    d.UserProfile != null &&
                    d.UserProfile.UserId == email);

            if (document == null)
                return NotFound();

            try
            {
                var bytes = await _documents.ReadAsync(
                    document,
                    HttpContext.RequestAborted);

                var filename = Path.GetFileName(
                    document.OriginalFileName ?? "document");

                return File(
                    bytes,
                    "application/octet-stream",
                    filename);
            }
            catch (FileNotFoundException)
            {
                return NotFound();
            }
            catch (CryptographicException)
            {
                return StatusCode(
                    500,
                    "Документът не може да бъде декриптиран.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            var email = CurrentEmail;

            if (string.IsNullOrWhiteSpace(email))
                return Challenge();

            var document = await _context.UserDocuments
                .Include(d => d.UserProfile)
                .FirstOrDefaultAsync(d =>
                    d.Id == id &&
                    d.UserProfile != null &&
                    d.UserProfile.UserId == email);

            if (document == null)
                return NotFound();

            _context.UserDocuments.Remove(document);
            await _context.SaveChangesAsync();

            try
            {
                _documents.Delete(document);
            }
            catch (IOException)
            {
                // Не показваме вътрешни файлови пътища.
            }

            TempData["SuccessMessage"] =
                "Документът е изтрит успешно.";

            return RedirectToAction(nameof(Index));
        }
    }
}
