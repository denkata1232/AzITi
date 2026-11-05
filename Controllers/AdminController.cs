using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NiePostigame.Data;
using NiePostigame.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NiePostigame.Controllers
{
    // TODO: когато добавиш роли, смени на [Authorize(Roles = "Admin")]
    [Authorize(Policy = "СамоАдминистратори")]
    public class AdminController : Controller
    {
        private static readonly string[] DateFormats = { "yyyy.M.d", "yyyy.MM.dd", "yyyy-MM-dd" };

        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Admin
        public async Task<IActionResult> Index()
        {
            var campaigns = await _context.AidCampaigns
                .Include(c => c.CampaignForms)
                .OrderByDescending(c => c.Id)
                .ToListAsync();

            return View(campaigns);
        }

        // GET: /Admin/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Admin/CreateManual (Ръчно въвеждане)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateManual(AidCampaign campaign, string? rawFormUrls, string[]? requiredDocumentTypes)
        {
            if (ModelState.IsValid)
            {
                ProcessFormUrls(campaign, rawFormUrls);
                foreach (var type in NormalizeDocumentTypes(requiredDocumentTypes))
                {
                    campaign.RequiredDocuments.Add(new CampaignRequiredDocument
                    {
                        DocumentType = type
                    });
                }
                _context.AidCampaigns.Add(campaign); // ContactEmail идва от модела
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Кампанията беше добавена успешно!";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.RawFormUrls = rawFormUrls;
            return View("Create", campaign);
        }

        // POST: /Admin/ParseText (Бърз внос)
        // Формат: Заглавие, Описание, Тип помощ, Изисквания, Лого, Период, Линкове, [Имейл за контакт]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ParseText(string rawText, string[]? requiredDocumentTypes)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                TempData["ErrorMessage"] = "Моля, въведете валиден текст.";
                return RedirectToAction(nameof(Create));
            }

            try
            {
                var parts = ParseCsvRow(rawText);

                if (parts.Count < 6)
                {
                    TempData["ErrorMessage"] = "Текстът не съдържа достатъчно елементи. Очаквани: Заглавие, Описание, Тип помощ, Изисквания, Лого, Период, Линкове, Имейл (по желание).";
                    return RedirectToAction(nameof(Create));
                }

                string title = parts[0];
                string description = parts[1];
                string aidType = parts[2];
                string requirement = parts[3];
                string logoUrl = parts[4];
                string datesRaw = parts[5];
                string linksRaw = parts.Count > 6 ? parts[6] : "";
                string? contactEmail = parts.Count > 7 && !string.IsNullOrWhiteSpace(parts[7]) ? parts[7] : null;

                DateTime validFrom = DateTime.Now;
                DateTime validTo = DateTime.Now.AddMonths(6);

                var dateParts = datesRaw.Split(new[] { '-', '—' }, StringSplitOptions.RemoveEmptyEntries);
                if (dateParts.Length == 2)
                {
                    string cleanStart = dateParts[0].Trim().TrimEnd('.');
                    string cleanEnd = dateParts[1].Trim().TrimEnd('.');

                    // Ползваме временни променливи, за да не се презапишат стойностите по подразбиране при неуспех
                    if (DateTime.TryParseExact(cleanStart, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFrom))
                        validFrom = parsedFrom;
                    if (DateTime.TryParseExact(cleanEnd, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTo))
                        validTo = parsedTo;
                }

                var newCampaign = new AidCampaign
                {
                    Title = title,
                    Description = description,
                    AidType = aidType,
                    SimplifiedRequirement = requirement,
                    LogoUrl = logoUrl,
                    ContactEmail = contactEmail,
                    ValidFrom = validFrom,
                    ValidTo = validTo
                };

                // Парсване на линкове във формат [Текст](URL) или стандартни URL-и
                if (!string.IsNullOrWhiteSpace(linksRaw))
                {
                    var matches = Regex.Matches(linksRaw, @"\[(.*?)\]\((.*?)\)");

                    if (matches.Count > 0)
                    {
                        foreach (Match match in matches)
                        {
                            string formName = match.Groups[1].Value.Trim();
                            string formUrl = match.Groups[2].Value.Trim();

                            newCampaign.CampaignForms.Add(new CampaignForm
                            {
                                Name = string.IsNullOrWhiteSpace(formName) ? "Формуляр" : formName,
                                FormUrl = formUrl
                            });
                        }
                    }
                    else
                    {
                        var links = linksRaw.Replace("(", "").Replace(")", "")
                            .Split(new[] { ' ', '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        int count = 1;
                        foreach (var link in links)
                        {
                            if (link.StartsWith("http") || link.StartsWith("www"))
                            {
                                newCampaign.CampaignForms.Add(new CampaignForm
                                {
                                    Name = $"Формуляр {count++}",
                                    FormUrl = link.Trim()
                                });
                            }
                        }
                    }
                }
                foreach (var type in NormalizeDocumentTypes(requiredDocumentTypes))
                {
                    newCampaign.RequiredDocuments.Add(
                        new CampaignRequiredDocument
                        {
                            DocumentType = type
                        });
                }
                _context.AidCampaigns.Add(newCampaign);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Кампанията \"{title}\" беше парсната и добавена успешно!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Грешка при обработката на текста: " + ex.Message;
                return RedirectToAction(nameof(Create));
            }
        }

        // GET: /Admin/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var campaign = await _context.AidCampaigns
                .Include(c => c.CampaignForms)
                .Include(c => c.RequiredDocuments)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (campaign == null)
            {
                return NotFound();
            }

            ViewBag.RawFormUrls = string.Join("\n", campaign.CampaignForms.Select(f => $"{f.Name} | {f.FormUrl}"));
            return View(campaign);
        }

        // POST: /Admin/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, AidCampaign campaign, string? rawFormUrls, string[]? requiredDocumentTypes)
        {
            if (id != campaign.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var existingCampaign = await _context.AidCampaigns
                    .Include(c => c.CampaignForms)
                    .Include(c => c.RequiredDocuments)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (existingCampaign == null)
                {
                    return NotFound();
                }

                existingCampaign.Title = campaign.Title;
                existingCampaign.Description = campaign.Description;
                existingCampaign.AidType = campaign.AidType;
                existingCampaign.SimplifiedRequirement = campaign.SimplifiedRequirement;
                existingCampaign.LogoUrl = campaign.LogoUrl;
                existingCampaign.ContactEmail = campaign.ContactEmail; // <-- липсваше
                existingCampaign.ValidFrom = campaign.ValidFrom;
                existingCampaign.ValidTo = campaign.ValidTo;
                // GoogleFormUrl умишлено не се пипа: във формата няма поле за него

                _context.CampaignForms.RemoveRange(existingCampaign.CampaignForms);
                existingCampaign.CampaignForms.Clear();
                ProcessFormUrls(existingCampaign, rawFormUrls);

                _context.CampaignRequiredDocuments
                    .RemoveRange(existingCampaign.RequiredDocuments);

                existingCampaign.RequiredDocuments.Clear();

                foreach (var type in NormalizeDocumentTypes(requiredDocumentTypes))
                {
                    existingCampaign.RequiredDocuments.Add(
                        new CampaignRequiredDocument
                        {
                            DocumentType = type
                        });
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Кампанията \"{campaign.Title}\" беше обновена успешно!";
                return RedirectToAction(nameof(Index));
            }

            // При грешка във валидацията запазваме въведените линкове във формата
            ViewBag.RawFormUrls = rawFormUrls;
            return View(campaign);
        }

        // POST: /Admin/Delete/5
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
                TempData["SuccessMessage"] = "Кампанията беше изтрита успешно.";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: /Admin/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var campaign = await _context.AidCampaigns
                .Include(c => c.CampaignForms)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (campaign == null)
            {
                return NotFound();
            }

            return View(campaign);
        }

        // Помощен метод за разпознаване на линкове във формат: Име | URL
        private void ProcessFormUrls(AidCampaign campaign, string? rawFormUrls)
        {
            if (string.IsNullOrWhiteSpace(rawFormUrls)) return;

            var lines = rawFormUrls.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            int defaultIndex = 1;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine)) continue;

                string name;
                string url;

                if (trimmedLine.Contains('|'))
                {
                    var parts = trimmedLine.Split('|', 2); // 2 = URL-ът може да съдържа '|'
                    name = parts[0].Trim();
                    url = parts[1].Trim();

                    if (string.IsNullOrWhiteSpace(name)) name = $"Формуляр {defaultIndex++}";
                }
                else
                {
                    name = $"Формуляр {defaultIndex++}";
                    url = trimmedLine;
                }

                if (string.IsNullOrWhiteSpace(url)) continue;

                campaign.CampaignForms.Add(new CampaignForm
                {
                    Name = name,
                    FormUrl = url
                });
            }
        }

        private List<string> ParseCsvRow(string row)
        {
            var results = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            foreach (char c in row)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    results.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0 && !string.IsNullOrWhiteSpace(current.ToString()))
            {
                results.Add(current.ToString().Trim());
            }

            return results;
        }
        private static List<string> NormalizeDocumentTypes(
    string[]? documentTypes)
        {
            var allowed = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
    {
        "Смъртен акт",
        "ТЕЛК",
        "Уверение за студент",
        "Уверение за ученик",
        "Друго"
    };

            return (documentTypes ?? Array.Empty<string>())
                .Select(x => x.Trim())
                .Where(allowed.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}