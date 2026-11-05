
using System.Security.Cryptography;
using System.Text;

namespace NiePostigame.Services
{
    public sealed class EncryptionService
    {
        private const string TextPrefix = "enc:v1:";
        private const int NonceSize = 12;
        private const int TagSize = 16;

        private readonly byte[] _key;

        public EncryptionService(IConfiguration configuration)
        {
            var keyBase64 = configuration["Encryption:MasterKey"];

            if (string.IsNullOrWhiteSpace(keyBase64))
                throw new InvalidOperationException(
                    "Липсва Encryption:MasterKey.");

            try
            {
                _key = Convert.FromBase64String(keyBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "Encryption:MasterKey не е валиден Base64.", ex);
            }

            if (_key.Length != 32)
                throw new InvalidOperationException(
                    "AES-256 изисква ключ от точно 32 байта.");
        }

        public string Encrypt(string plaintext)
        {
            ArgumentNullException.ThrowIfNull(plaintext);

            var bytes = Encoding.UTF8.GetBytes(plaintext);
            return TextPrefix +
                Convert.ToBase64String(EncryptBytes(bytes));
        }

        public string Decrypt(string ciphertext)
        {
            ArgumentNullException.ThrowIfNull(ciphertext);

            if (!ciphertext.StartsWith(
                    TextPrefix, StringComparison.Ordinal))
            {
                throw new CryptographicException(
                    "Стойността не е криптирана.");
            }

            byte[] encrypted;

            try
            {
                encrypted = Convert.FromBase64String(
                    ciphertext[TextPrefix.Length..]);
            }
            catch (FormatException ex)
            {
                throw new CryptographicException(
                    "Невалиден формат на криптираните данни.", ex);
            }

            return Encoding.UTF8.GetString(
                DecryptBytes(encrypted));
        }

        public byte[] EncryptBytes(byte[] plaintext)
        {
            ArgumentNullException.ThrowIfNull(plaintext);

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var ciphertext = new byte[plaintext.Length];

            using (var aes = new AesGcm(_key, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            var result = new byte[
                NonceSize + TagSize + ciphertext.Length];

            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
            Buffer.BlockCopy(
                ciphertext, 0, result,
                NonceSize + TagSize, ciphertext.Length);

            return result;
        }

        public byte[] DecryptBytes(byte[] encrypted)
        {
            ArgumentNullException.ThrowIfNull(encrypted);

            if (encrypted.Length < NonceSize + TagSize)
                throw new CryptographicException(
                    "Криптираните данни са непълни.");

            var nonce = encrypted[..NonceSize];
            var tag = encrypted[
                NonceSize..(NonceSize + TagSize)];
            var ciphertext = encrypted[
                (NonceSize + TagSize)..];

            var plaintext = new byte[ciphertext.Length];

            using (var aes = new AesGcm(_key, TagSize))
            {
                aes.Decrypt(
                    nonce, ciphertext, tag, plaintext);
            }

            return plaintext;
        }
    }
}
