
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NiePostigame.Data;

namespace NiePostigame.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Последните две активни кампании
            var recentCampaigns = await _context.AidCampaigns
                .Include(c => c.CampaignForms)
                .Where(c =>
                    DateTime.Now >= c.ValidFrom &&
                    DateTime.Now <= c.ValidTo)
                .Take(2)
                .ToListAsync();

            ViewBag.ActiveCampaignsCount =
                await _context.AidCampaigns.CountAsync(c =>
                    DateTime.Now >= c.ValidFrom &&
                    DateTime.Now <= c.ValidTo);

            return View(recentCampaigns);
        }

        public IActionResult About()
        {
            return View();
        }

        public IActionResult Partners()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        // Страница при отказан достъп
        [HttpGet]
        public IActionResult AccessDenied()
        {
            Response.StatusCode = 403;
            return View();
        }
    }
}
