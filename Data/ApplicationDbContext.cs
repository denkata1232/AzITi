
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NiePostigame.Models;
using NiePostigame.Services;

namespace NiePostigame.Data
{
    public class ApplicationDbContext : DbContext
    {
        private readonly EncryptionService _encryption;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            EncryptionService encryption)
            : base(options)
        {
            _encryption = encryption;
        }

        public DbSet<AidCampaign> AidCampaigns { get; set; } = null!;
        public DbSet<CampaignForm> CampaignForms { get; set; } = null!;
        public DbSet<UserGmailProfile> UserGmailProfiles { get; set; } = null!;
        public DbSet<UserProfile> UserProfiles { get; set; } = null!;
        public DbSet<UserDocument> UserDocuments { get; set; } = null!;
        public DbSet<UserApplication> UserApplications { get; set; } = null!;
        public DbSet<CampaignRequiredDocument> CampaignRequiredDocuments
    => Set<CampaignRequiredDocument>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // EncryptionService е singleton.
            // Не прихващаме инстанцията на DbContext.
            var encryption = _encryption;

            var converter =
                new ValueConverter<string?, string?>(
                    value => value == null
                        ? null
                        : encryption.Encrypt(value),

                    value => value == null
                        ? null
                        : encryption.Decrypt(value)
                );

            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.Property(p => p.FirstName)
                    .HasConversion(converter)
                    .HasColumnType("nvarchar(max)");

                entity.Property(p => p.MiddleName)
                    .HasConversion(converter)
                    .HasColumnType("nvarchar(max)");

                entity.Property(p => p.LastName)
                    .HasConversion(converter)
                    .HasColumnType("nvarchar(max)");

                entity.Property(p => p.Egn)
                    .HasConversion(converter)
                    .HasColumnType("nvarchar(max)");

                entity.Property(p => p.Iban)
                    .HasConversion(converter)
                    .HasColumnType("nvarchar(max)");

                entity.Property(p => p.PhoneNumber)
                    .HasConversion(converter)
                    .HasColumnType("nvarchar(max)");
            });

            modelBuilder.Entity<UserGmailProfile>(entity =>
            {
                entity.Property(p => p.AccessToken)
                    .HasConversion(converter)
                    .HasColumnType("nvarchar(max)");

                entity.Property(p => p.RefreshToken)
                    .HasConversion(converter)
                    .HasColumnType("nvarchar(max)");
            });
        }
    }
}
