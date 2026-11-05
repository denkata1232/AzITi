
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using NiePostigame.Data;
using NiePostigame.Services;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// 1. АДМИНИСТРАТОРСКИ АКАУНТИ
// ============================================

var adminEmails = builder.Configuration
    .GetSection("AdminSettings:Emails")
    .Get<string[]>() ?? Array.Empty<string>();

var allowedAdmins = new HashSet<string>(
    adminEmails
        .Where(email => !string.IsNullOrWhiteSpace(email))
        .Select(email => email.Trim()),
    StringComparer.OrdinalIgnoreCase
);

// ============================================
// 2. КРИПТИРАНЕ
// ============================================

// Проверяваме ключа при стартиране.
var encryptionKey = builder.Configuration[
    "Encryption:MasterKey"];

if (string.IsNullOrWhiteSpace(encryptionKey))
{
    throw new InvalidOperationException(
        "Липсва Encryption:MasterKey в конфигурацията.");
}

builder.Services.AddSingleton<EncryptionService>();
builder.Services.AddScoped<SecureDocumentService>();

// ============================================
// 3. БАЗА ДАННИ
// ============================================

builder.Services.AddDbContext<ApplicationDbContext>(
    options =>
    {
        options.UseSqlServer(
            builder.Configuration.GetConnectionString(
                "DSAConnection"));
    });

// ============================================
// 4. СЕСИИ
// ============================================

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy =
        CookieSecurePolicy.Always;
    options.Cookie.SameSite =
        SameSiteMode.Lax;
});
builder.Services.AddScoped<CampaignDocumentService>();

// ============================================
// 5. GOOGLE AUTHENTICATION
// ============================================

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            CookieAuthenticationDefaults.AuthenticationScheme;

        options.DefaultSignInScheme =
            CookieAuthenticationDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme =
            GoogleDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "NiePostigame.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy =
            CookieSecurePolicy.Always;

        options.Cookie.SameSite =
            SameSiteMode.Lax;

        options.ExpireTimeSpan =
            TimeSpan.FromHours(8);

        options.SlidingExpiration = true;

        options.AccessDeniedPath =
            "/Home/AccessDenied";
    })
    .AddGoogle(options =>
    {
        options.ClientId =
            builder.Configuration[
                "Authentication:Google:ClientId"
            ] ?? throw new InvalidOperationException(
                "Липсва Google Client ID.");

        options.ClientSecret =
            builder.Configuration[
                "Authentication:Google:ClientSecret"
            ] ?? throw new InvalidOperationException(
                "Липсва Google Client Secret.");

        options.CallbackPath = "/signin-google";

        options.Scope.Add(
            "https://www.googleapis.com/auth/gmail.send");

        // Не го изключваме, защото GoogleResponse
        // използва токените от authentication ticket.
        options.SaveTokens = true;
        options.AccessType = "offline";

        options.Events.OnCreatingTicket = context =>
        {
            var accessToken = context.AccessToken;
            var refreshToken = context.RefreshToken;

            var email = context.Principal?
                .FindFirst(ClaimTypes.Email)?.Value;

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                context.HttpContext.Session.SetString(
                    "GoogleAccessToken",
                    accessToken);
            }

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                context.HttpContext.Session.SetString(
                    "GoogleRefreshToken",
                    refreshToken);
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                context.HttpContext.Session.SetString(
                    "GoogleEmail",
                    email);
            }

            return Task.CompletedTask;
        };
    });

// ============================================
// 6. АДМИНИСТРАТОРСКА ЗАЩИТА
// ============================================

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        "СамоАдминистратори",
        policy =>
        {
            policy.RequireAuthenticatedUser();

            policy.RequireAssertion(context =>
            {
                var email = context.User
                    .FindFirstValue(ClaimTypes.Email);

                return !string.IsNullOrWhiteSpace(email)
                    && allowedAdmins.Contains(email);
            });
        });
});

// ============================================
// 7. MVC
// ============================================

builder.Services.AddControllersWithViews(options =>
{
    options.Conventions.Add(
        new AdminAuthorizationConvention());
});

builder.Services.AddHttpClient();

// ============================================
// 8. СТАРТИРАНЕ
// ============================================

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern:
        "{controller=Home}/{action=Index}/{id?}");

app.Run();

// ============================================
// 9. ADMIN CONTROLLER POLICY
// ============================================

public sealed class AdminAuthorizationConvention
    : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        if (controller.ControllerName.Equals(
            "Admin",
            StringComparison.OrdinalIgnoreCase))
        {
            controller.Filters.Add(
                new AuthorizeFilter(
                    "СамоАдминистратори"));
        }
    }
}
