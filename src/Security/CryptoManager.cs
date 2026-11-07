using System;
using System.Security.Cryptography;
using System.Text;
using PCFreeze.Models;

namespace PCFreeze.Security
{
    public static class CryptoManager
    {
        private const int SaltSizeBytes = 32;
        private const int HashSizeBytes = 32;
        private const int Iterations = 50000;

        public static void SetPassword(PCFreezeConfig config, string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword))
            {
                config.PasswordHash = "";
                config.PasswordSalt = "";
                return;
            }

            byte[] salt = new byte[SaltSizeBytes];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }

            using (Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(plainPassword, salt, Iterations))
            {
                byte[] hash = pbkdf2.GetBytes(HashSizeBytes);
                config.PasswordSalt = Convert.ToBase64String(salt);
                config.PasswordHash = Convert.ToBase64String(hash);
            }
        }

        public static bool VerifyPasswordOrOtp(PCFreezeConfig config, string input)
        {
            // Si aucun mot de passe n'est encore configuré (1er lancement), on autorise l'ouverture
            // que le champ soit laissé vide ou que l'utilisateur ait saisi quelque chose.
            if (string.IsNullOrEmpty(config.PasswordHash))
            {
                return true;
            }

            if (string.IsNullOrEmpty(input))
                return false;

            // 1. Vérification du mot de passe maître
            try
            {
                byte[] salt = Convert.FromBase64String(config.PasswordSalt);
                byte[] expectedHash = Convert.FromBase64String(config.PasswordHash);

                using (Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(input, salt, Iterations))
                {
                    byte[] actualHash = pbkdf2.GetBytes(HashSizeBytes);
                    if (ConstantTimeEquals(expectedHash, actualHash))
                        return true;
                }
            }
            catch
            {
                // En cas de sel corrompu, passer à la vérification OTP
            }

            // 2. Vérification du Jeton à Usage Unique (OTP - One-Time Password comme dans Deep Freeze)
            string normalizedInput = input.Trim().Replace("-", "").ToUpperInvariant();
            for (int hourOffset = -1; hourOffset <= 1; hourOffset++)
            {
                string expectedOtp = GenerateOtpForHour(config.StationName, config.OtpSecretSeed, DateTime.UtcNow.AddHours(hourOffset))
                    .Replace("-", "");
                if (string.Equals(normalizedInput, expectedOtp, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string GenerateCurrentOtp(string stationName, string otpSecretSeed)
        {
            return GenerateOtpForHour(stationName, otpSecretSeed, DateTime.UtcNow);
        }

        public static string GenerateOtpForHour(string stationName, string otpSecretSeed, DateTime utcTime)
        {
            string station = (stationName ?? "STATION").Trim().ToUpperInvariant();
            string seed = (otpSecretSeed ?? "DEFAULTSEED").Trim().ToUpperInvariant();
            string timeBucket = utcTime.ToString("yyyyMMddHH");
            string payload = string.Format("{0}|{1}|{2}", station, seed, timeBucket);

            using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(seed)))
            {
                byte[] digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
                StringBuilder sb = new StringBuilder(9);
                for (int i = 0; i < 8; i++)
                {
                    if (i == 4) sb.Append('-');
                    int idx = digest[i] % alphabet.Length;
                    sb.Append(alphabet[idx]);
                }
                return sb.ToString();
            }
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }
    }
}
