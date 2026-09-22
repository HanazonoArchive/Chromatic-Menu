using System;
using System.Security.Cryptography;
using System.Text;

namespace ChromaticMenu.Services
{
    public class PasswordService
    {
        // SHA-256 of "admin"
        public const string DefaultPasswordHash = "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918";

        // Hard-coded recovery hash per user request: SHA-256 of "default"
        public const string RecoveryPasswordHash = "37a8eec1ce19687d132fe29051dca629d164e2c4958ba141d5f4133a33f0688f";

        private static PasswordService _instance;
        public static PasswordService Instance => _instance ?? (_instance = new PasswordService());

        public string ComputeHash(string password)
        {
            if (password == null) password = string.Empty;

            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(password);
                byte[] hash = sha256.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        public bool VerifyPassword(string inputPassword, string storedPasswordHash)
        {
            if (string.IsNullOrEmpty(inputPassword)) return false;

            // SPEC section 8: accepted only if the SHA-256 of the input matches the stored
            // hash or the hard-coded recovery hash exactly. No case-folding of the password.
            string inputHash = ComputeHash(inputPassword);

            bool matchesStored = !string.IsNullOrEmpty(storedPasswordHash) &&
                                  string.Equals(inputHash, storedPasswordHash, StringComparison.OrdinalIgnoreCase);

            bool matchesRecovery = !string.IsNullOrEmpty(RecoveryPasswordHash) &&
                                    string.Equals(inputHash, RecoveryPasswordHash, StringComparison.OrdinalIgnoreCase);

            return matchesStored || matchesRecovery;
        }

        public bool IsRecoveryPassword(string inputPassword)
        {
            if (string.IsNullOrEmpty(inputPassword)) return false;
            string inputHash = ComputeHash(inputPassword);
            return string.Equals(inputHash, RecoveryPasswordHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
