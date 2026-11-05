
using NiePostigame.Models;
using System.Security.Cryptography;

namespace NiePostigame.Services
{
    public sealed class SecureDocumentService
    {
        private const string Prefix = "secure:v1:";
        public const long MaxFileSize = 10 * 1024 * 1024;

        private readonly EncryptionService _encryption;
        private readonly string _storagePath;

        public SecureDocumentService(
            EncryptionService encryption,
            IWebHostEnvironment environment)
        {
            _encryption = encryption;

            // Създаваме папката извън wwwroot.
            _storagePath = Path.Combine(
                environment.ContentRootPath,
                "SecureDocuments");

            Directory.CreateDirectory(_storagePath);
        }

        private string ResolvePath(string reference)
        {
            if (!reference.StartsWith(
                    Prefix, StringComparison.Ordinal))
            {
                throw new CryptographicException(
                    "Неподдържан формат на документ.");
            }

            var filename = reference[Prefix.Length..];

            if (filename.Length != 36 ||
                !filename.EndsWith(
                    ".enc", StringComparison.Ordinal) ||
                !Guid.TryParseExact(
                    filename[..32], "N", out _))
            {
                throw new CryptographicException(
                    "Невалиден идентификатор на документ.");
            }

            return Path.Combine(_storagePath, filename);
        }

        public async Task<string> SaveAsync(
            IFormFile file,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);

            if (file.Length <= 0 ||
                file.Length > MaxFileSize)
            {
                throw new InvalidOperationException(
                    "Документът трябва да е до 10 MB.");
            }

            await using var input = file.OpenReadStream();

            using var memory = new MemoryStream();
            await input.CopyToAsync(
                memory, cancellationToken);

            if (memory.Length == 0 ||
                memory.Length > MaxFileSize)
            {
                throw new InvalidOperationException(
                    "Невалиден размер на документа.");
            }

            var plaintext = memory.ToArray();

            // Проверка на реалния файлов формат.
            var extension = Path.GetExtension(
                file.FileName).ToLowerInvariant();

            bool valid = extension switch
            {
                ".pdf" => plaintext.Length >= 5 &&
                    plaintext.AsSpan(0, 5)
                        .SequenceEqual("%PDF-"u8),

                ".png" => plaintext.Length >= 8 &&
                    plaintext.AsSpan(0, 8)
                        .SequenceEqual(
                            new byte[] {
                                137, 80, 78, 71,
                                13, 10, 26, 10
                            }),

                ".jpg" or ".jpeg" =>
                    plaintext.Length >= 3 &&
                    plaintext[0] == 0xFF &&
                    plaintext[1] == 0xD8 &&
                    plaintext[2] == 0xFF,

                _ => false
            };

            if (!valid)
            {
                throw new InvalidOperationException(
                    "Позволени са само валидни PDF, JPG и PNG файлове.");
            }

            var encrypted = _encryption.EncryptBytes(plaintext);
            var filename = Guid.NewGuid().ToString("N") + ".enc";
            var reference = Prefix + filename;
            var destination = ResolvePath(reference);

            await using (var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                await output.WriteAsync(
                    encrypted, cancellationToken);
            }

            return reference;
        }

        public async Task<byte[]> ReadAsync(
            UserDocument document,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(document.FilePath))
                throw new FileNotFoundException(
                    "Липсва път към документа.");

            var path = ResolvePath(document.FilePath);

            var info = new FileInfo(path);

            if (!info.Exists)
                throw new FileNotFoundException(
                    "Документът не е намерен.");

            if (info.Length > MaxFileSize + 28)
                throw new InvalidOperationException(
                    "Документът надвишава допустимия размер.");

            var encrypted = await File.ReadAllBytesAsync(
                path, cancellationToken);

            return _encryption.DecryptBytes(encrypted);
        }

        public void Delete(UserDocument document)
        {
            if (string.IsNullOrWhiteSpace(document.FilePath))
                return;

            var path = ResolvePath(document.FilePath);

            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
