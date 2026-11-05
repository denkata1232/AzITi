
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NiePostigame.Data;
using NiePostigame.Models;
using System.Security.Claims;

namespace NiePostigame.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login(string returnUrl = "/")
        {
            if (!Url.IsLocalUrl(returnUrl))
                returnUrl = "/";

            var redirectUrl = Url.Action(
                nameof(GoogleResponse),
                "Account",
                new { returnUrl });

            var properties = new AuthenticationProperties
            {
                RedirectUri = redirectUrl
            };

            properties.Items["prompt"] = "consent";

            return Challenge(
                properties,
                GoogleDefaults.AuthenticationScheme);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GoogleResponse(
            string returnUrl = "/")
        {
            var result = await HttpContext.AuthenticateAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            if (!result.Succeeded || result.Principal == null)
            {
                TempData["ErrorMessage"] =
                    "Грешка при автентикация с Google.";

                return RedirectToAction("Index", "Home");
            }

            var email = result.Principal.FindFirstValue(
                ClaimTypes.Email);

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] =
                    "Google не предостави имейл адрес.";

                return RedirectToAction("Index", "Home");
            }

            var accessToken = result.Properties?
                .GetTokenValue("access_token");

            var refreshToken = result.Properties?
                .GetTokenValue("refresh_token");

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                HttpContext.Session.SetString(
                    "GoogleAccessToken", accessToken);
            }

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                HttpContext.Session.SetString(
                    "GoogleRefreshToken", refreshToken);
            }

            HttpContext.Session.SetString(
                "GoogleEmail", email);

            var profile = await _context.UserGmailProfiles
                .FirstOrDefaultAsync(p => p.GmailAddress == email);

            if (profile == null)
            {
                profile = new UserGmailProfile
                {
                    UserId = email,
                    GmailAddress = email,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    TokenExpiresAt = DateTime.UtcNow.AddHours(1),
                    IsConnected =
                        !string.IsNullOrWhiteSpace(accessToken),
                    ConnectedAt = DateTime.UtcNow
                };

                _context.UserGmailProfiles.Add(profile);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    profile.AccessToken = accessToken;
                    profile.TokenExpiresAt =
                        DateTime.UtcNow.AddHours(1);
                }

                if (!string.IsNullOrWhiteSpace(refreshToken))
                    profile.RefreshToken = refreshToken;

                profile.IsConnected =
                    !string.IsNullOrWhiteSpace(profile.AccessToken);
            }

            await _context.SaveChangesAsync();

            if (!Url.IsLocalUrl(returnUrl))
                returnUrl = "/";

            return LocalRedirect(returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            HttpContext.Session.Clear();

            return RedirectToAction("Index", "Home");
        }
    }
}
