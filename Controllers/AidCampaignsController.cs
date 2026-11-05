using NiePostigame.Data;
using NiePostigame.Models;
using NiePostigame.Services;
using System.Security.Claims;
using NiePostigame.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;

namespace NiePostigame.Controllers
{
    public class AidCampaignsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly CampaignDocumentService _documentService;

        public AidCampaignsController(
            ApplicationDbContext context,
            CampaignDocumentService documentService)
        {
            _context = context;
            _documentService = documentService;
        }

        // GET: /AidCampaigns
        public async Task<IActionResult> Index()
        {
            var campaigns = await _context.AidCampaigns
                .Include(c => c.CampaignForms)
                .ToListAsync();

            var viewModelList = campaigns.Select(c => new CampaignListViewModel
            {
                Id = c.Id,
                Title = c.Title ?? string.Empty,
                Description = c.Description ?? string.Empty,
                LogoUrl = c.LogoUrl,
                ContactEmail = c.ContactEmail,
                SimplifiedRequirement = c.SimplifiedRequirement ?? string.Empty,
                ValidFrom = c.ValidFrom,
                ValidTo = c.ValidTo,
                CampaignForms = c.CampaignForms?.ToList() ?? new List<CampaignForm>(),
                IsActive = DateTime.Now >= c.ValidFrom && DateTime.Now <= c.ValidTo,
                StatusMessage = DateTime.Now < c.ValidFrom ? "Предстои" : (DateTime.Now <= c.ValidTo ? "Активна" : "Изтекла")
            }).ToList();

            return View(viewModelList);
        }

        // GET: /AidCampaigns/Details/1
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var campaign = await _context.AidCampaigns
                .Include(c => c.CampaignForms)
                .Include(c => c.RequiredDocuments)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (campaign == null)
            {
                TempData["ErrorMessage"] = $"Кампания с номер #{id} не беше намерена.";
                return RedirectToAction(nameof(Index));
            }

            var userEmail = User.FindFirstValue(ClaimTypes.Email);

            ViewBag.MissingDocuments = new List<string>();

            if (!string.IsNullOrWhiteSpace(userEmail))
            {
                ViewBag.MissingDocuments =
                    await _documentService.GetMissingDocumentsAsync(
                        id, userEmail);
            }

            userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (!string.IsNullOrEmpty(userEmail))
            {
                ViewBag.HasApplied = await _context.UserApplications
                    .AnyAsync(a => a.UserId == userEmail && a.CampaignId == id);
            }
            else
            {
                ViewBag.HasApplied = false;
            }

            return View(campaign);
        }

        // GET: /AidCampaigns/Finder
        [HttpGet]
        public IActionResult Finder()
        {
            return View();
        }

        // POST: /AidCampaigns/Match
        [HttpPost]
        public async Task<IActionResult> Match(string? educationLevel, string? aidType, string? keyword)
        {
            var query = _context.AidCampaigns
                .Include(c => c.CampaignForms)
                .Where(c => DateTime.Now >= c.ValidFrom && DateTime.Now <= c.ValidTo)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(educationLevel) && educationLevel != "All")
            {
                string searchTerm = educationLevel switch
                {
                    "HighSchoolers" => "ученик",
                    "Students" => "студент",
                    _ => educationLevel
                };

                query = query.Where(c => 
                    (c.Title != null && c.Title.Contains(searchTerm)) || 
                    (c.Description != null && c.Description.Contains(searchTerm)) || 
                    (c.SimplifiedRequirement != null && c.SimplifiedRequirement.Contains(searchTerm)));
            }

            if (!string.IsNullOrWhiteSpace(aidType))
            {
                query = query.Where(c => c.AidType == aidType);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(c => 
                    (c.Title != null && c.Title.Contains(keyword)) || 
                    (c.Description != null && c.Description.Contains(keyword)));
            }

            var matchedCampaigns = await query.ToListAsync();

            ViewBag.EducationLevel = educationLevel;
            ViewBag.AidType = aidType;
            ViewBag.Keyword = keyword;

            return View("MatchResults", matchedCampaigns);
        }

        // ==========================================
        // АДМИНСКИ МЕТОДИ ЗА СЪЗДАВАНЕ И РЕДАКТИРАНЕ
        // ==========================================

        // GET: /AidCampaigns/Create
        [Authorize]
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // POST: /AidCampaigns/Create
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AidCampaign campaign, string? rawFormUrls)
        {
            if (ModelState.IsValid)
            {
                if (!string.IsNullOrWhiteSpace(rawFormUrls))
                {
                    ParseAndAddForms(campaign, rawFormUrls);
                }

                _context.Add(campaign);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Кампанията беше създадена успешно!";
                return RedirectToAction(nameof(Index));
            }

            return View(campaign);
        }

        // POST: /AidCampaigns/CreateManual
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateManual(AidCampaign campaign, string? rawFormUrls)
        {
            return await Create(campaign, rawFormUrls);
        }

        // POST: /AidCampaigns/ParseText
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ParseText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                TempData["ErrorMessage"] = "Моля, въведете текст за парсване.";
                return RedirectToAction(nameof(Create));
            }

            try
            {
                var parts = rawText.Split(new[] { " , ", "\", \"" }, StringSplitOptions.None);

                var campaign = new AidCampaign
                {
                    Title = parts.Length > 0 ? parts[0].Trim('"', ' ') : "Нова кампания",
                    Description = parts.Length > 1 ? parts[1].Trim('"', ' ') : "",
                    AidType = parts.Length > 2 ? parts[2].Trim('"', ' ') : "Обща",
                    SimplifiedRequirement = parts.Length > 3 ? parts[3].Trim('"', ' ') : "",
                    LogoUrl = parts.Length > 4 ? parts[4].Trim('"', ' ') : "",
                    ValidFrom = DateTime.Now,
                    ValidTo = DateTime.Now.AddMonths(3)
                };

                // Парсване на датите (YYYY.M.D-YYYY.M.D)
                if (parts.Length > 5 && parts[5].Contains("-"))
                {
                    var dates = parts[5].Split('-');
                    if (DateTime.TryParse(dates[0].Replace('.', '-'), out var from)) campaign.ValidFrom = from;
                    if (DateTime.TryParse(dates[1].Replace('.', '-'), out var to)) campaign.ValidTo = to;
                }

                // Парсване на линковете в Markdown формат [Име](URL)
                if (parts.Length > 6)
                {
                    var matches = Regex.Matches(parts[6], @"\[(.*?)\]\((.*?)\)");
                    foreach (Match match in matches)
                    {
                        if (match.Success)
                        {
                            campaign.CampaignForms.Add(new CampaignForm
                            {
                                Name = match.Groups[1].Value,
                                FormUrl = match.Groups[2].Value
                            });
                        }
                    }
                }

                // Прихващане на имейла за контакт (ако е подаден като 8-ми елемент)
                if (parts.Length > 7)
                {
                    var parsedEmail = parts[7].Trim('"', ' ');
                    if (!string.IsNullOrEmpty(parsedEmail) && parsedEmail.Contains("@"))
                    {
                        campaign.ContactEmail = parsedEmail;
                    }
                }

                _context.Add(campaign);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Кампанията беше импортирана успешно!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Възникна грешка при парсването: " + ex.Message;
                return RedirectToAction(nameof(Create));
            }
        }

        // GET: /AidCampaigns/Edit/5
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var campaign = await _context.AidCampaigns
                .Include(c => c.CampaignForms)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (campaign == null)
            {
                return NotFound();
            }

            if (campaign.CampaignForms != null && campaign.CampaignForms.Any())
            {
                ViewBag.RawFormUrls = string.Join("\n", campaign.CampaignForms.Select(f => $"{f.Name} | {f.FormUrl}"));
            }
            else
            {
                ViewBag.RawFormUrls = "";
            }

            return View(campaign);
        }

        // POST: /AidCampaigns/Edit/5
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, AidCampaign campaign, string? rawFormUrls)
        {
            if (id != campaign.Id)
            {
                return NotFound();
            }

            var existingCampaign = await _context.AidCampaigns
                .Include(c => c.CampaignForms)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (existingCampaign == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    existingCampaign.Title = campaign.Title;
                    existingCampaign.Description = campaign.Description;
                    existingCampaign.SimplifiedRequirement = campaign.SimplifiedRequirement;
                    existingCampaign.AidType = campaign.AidType;
                    existingCampaign.LogoUrl = campaign.LogoUrl;
                    existingCampaign.ContactEmail = campaign.ContactEmail;
                    existingCampaign.ValidFrom = campaign.ValidFrom;
                    existingCampaign.ValidTo = campaign.ValidTo;

                    _context.CampaignForms.RemoveRange(existingCampaign.CampaignForms);

                    if (!string.IsNullOrWhiteSpace(rawFormUrls))
                    {
                        ParseAndAddForms(existingCampaign, rawFormUrls);
                    }

                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Промените по кампанията бяха запазени успешно!";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AidCampaignExists(campaign.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            ViewBag.RawFormUrls = rawFormUrls;
            return View(campaign);
        }

        // POST: /AidCampaigns/Delete/5
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var campaign = await _context.AidCampaigns
                .Include(c => c.CampaignForms)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (campaign != null)
            {
                _context.CampaignForms.RemoveRange(campaign.CampaignForms);
                _context.AidCampaigns.Remove(campaign);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Кампанията беше изтрита успешно!";
            }

            return RedirectToAction(nameof(Index));
        }

        // Помощен метод за парсване и добавяне на формуляри към кампанията
        private void ParseAndAddForms(AidCampaign campaign, string rawFormUrls)
        {
            var lines = rawFormUrls.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length == 2)
                {
                    campaign.CampaignForms.Add(new CampaignForm
                    {
                        Name = parts[0].Trim(),
                        FormUrl = parts[1].Trim()
                    });
                }
                else if (parts.Length == 1 && Uri.IsWellFormedUriString(parts[0].Trim(), UriKind.Absolute))
                {
                    campaign.CampaignForms.Add(new CampaignForm
                    {
                        Name = "Формуляр за кандидатстване",
                        FormUrl = parts[0].Trim()
                    });
                }
            }
        }

        private bool AidCampaignExists(int id)
        {
            return _context.AidCampaigns.Any(e => e.Id == id);
        }
    }
}