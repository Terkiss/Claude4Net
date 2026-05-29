using System;
using System.Security.Cryptography;
using System.Text;

namespace Claude4Net.Runtime.Security
{
    public static class Cryptography
    {
        private static string _secret = "Claude4NetAndroidAuthDefaultHMACSecretString";

        public static string Secret
        {
            get => _secret;
            set => _secret = value;
        }

        public static string ComputeHmacSha256(string input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            
            byte[] keyBytes = Encoding.UTF8.GetBytes(_secret);
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);

            using (var hmac = new HMACSHA256(keyBytes))
            {
                byte[] hashBytes = hmac.ComputeHash(inputBytes);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
        }
    }
}
