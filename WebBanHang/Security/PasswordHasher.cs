using System;
using System.Security.Cryptography;
using System.Text;

namespace WebBanHang.Security
{
    public static class PasswordHasher
    {
        private const int Iterations = 210000;
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const string Prefix = "PBKDF2-SHA256";

        public static string Hash(string password)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password is required.", nameof(password));

            var salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);

            byte[] hash;
            using (var derive = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
            {
                hash = derive.GetBytes(HashSize);
            }

            return string.Join("$", Prefix, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
        }

        public static bool Verify(string password, string storedHash, out bool needsUpgrade)
        {
            needsUpgrade = false;
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash)) return false;

            if (!storedHash.StartsWith(Prefix + "$", StringComparison.Ordinal))
            {
                needsUpgrade = true;
                return FixedTimeEquals(LegacySha256(password), storedHash.ToLowerInvariant());
            }

            var parts = storedHash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations)) return false;

            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                byte[] actual;
                using (var derive = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                {
                    actual = derive.GetBytes(expected.Length);
                }
                needsUpgrade = iterations < Iterations;
                return FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static string LegacySha256(string password)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
                var result = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes) result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            return FixedTimeEquals(Encoding.UTF8.GetBytes(left ?? string.Empty), Encoding.UTF8.GetBytes(right ?? string.Empty));
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            var diff = left.Length ^ right.Length;
            var length = Math.Min(left.Length, right.Length);
            for (var i = 0; i < length; i++) diff |= left[i] ^ right[i];
            return diff == 0;
        }
    }
}
