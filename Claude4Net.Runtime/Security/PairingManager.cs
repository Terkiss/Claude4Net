using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Claude4Net.Runtime.Storage;

namespace Claude4Net.Runtime.Security
{
    public class PairingManager
    {
        private static readonly TimeSpan PairingTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(3);
        private static readonly TimeSpan SlidingWindow = TimeSpan.FromDays(1);

        public static async Task<(string PairingId, DateTime ExpiresAt, string Code)> CreatePairingRequestAsync(string deviceName, string appInstanceId)
        {
            // 1. Generate 10-digit PIN
            byte[] randomBytes = new byte[8];
            RandomNumberGenerator.Fill(randomBytes);
            long randomValue = BitConverter.ToInt64(randomBytes, 0);
            long pinValue = Math.Abs(randomValue) % 10000000000L;
            string pin = pinValue.ToString("D10");

            // 2. Hash the PIN
            string codeHash = Cryptography.ComputeHmacSha256(pin);

            // 3. Set expiration
            DateTime expiresAt = DateTime.UtcNow.Add(PairingTimeout);
            string pairingId = "pair_" + Guid.NewGuid().ToString("N").Substring(0, 12);

            // 4. Save to database
            using (var db = new AuthDatabase())
            {
                
                db.AndroidPairingRequests.Add(new AndroidPairingRequest
                {
                    PairingId = pairingId,
                    DeviceName = deviceName,
                    AppInstanceId = appInstanceId,
                    CodeHash = codeHash,
                    CreatedAt = DateTime.UtcNow.ToString("O"),
                    ExpiresAt = expiresAt.ToString("O"),
                    AttemptCount = 0,
                    Status = "Pending"
                });
                await db.SaveChangesAsync();
            }

            // 5. Print to console
            Console.WriteLine("[Android Pairing]");
            Console.WriteLine($"Device requested access: {deviceName}");
            Console.WriteLine($"Pairing code: {pin}");
            Console.WriteLine("Expires in: 30 seconds");

            return (pairingId, expiresAt, pin);
        }

        public static async Task<(bool Success, string Message, TokenResponse? Token)> ConfirmPairingAsync(string pairingId, string code, string clientIp)
        {
            using var db = new AuthDatabase();
            

            var req = await db.AndroidPairingRequests.FirstOrDefaultAsync(r => r.PairingId == pairingId);
            if (req == null) return (false, "Pairing request not found.", null);

            if (req.Status != "Pending") return (false, $"Pairing request is already {req.Status}.", null);

            if (DateTime.TryParse(req.ExpiresAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
            {
                if (DateTime.UtcNow > expiresAt.ToUniversalTime())
                {
                    req.Status = "Expired";
                    await db.SaveChangesAsync();
                    return (false, "Pairing code has expired.", null);
                }
            }

            string codeHash = Cryptography.ComputeHmacSha256(code);
            if (req.CodeHash != codeHash)
            {
                req.AttemptCount++;
                if (req.AttemptCount >= 5)
                {
                    req.Status = "Failed";
                    await db.SaveChangesAsync();
                    return (false, "Too many failed attempts. Pairing request blocked.", null);
                }
                await db.SaveChangesAsync();
                return (false, "Invalid pairing code.", null);
            }

            // Success
            req.Status = "Approved";
            
            // Generate token
            string token = "at_" + Guid.NewGuid().ToString("N");
            string tokenHash = Cryptography.ComputeHmacSha256(token);
            DateTime tokenExpiresAt = DateTime.UtcNow.Add(TokenLifetime);

            var authToken = new AndroidAuthToken
            {
                TokenId = "tok_" + Guid.NewGuid().ToString("N"),
                DeviceName = req.DeviceName,
                AppInstanceId = req.AppInstanceId,
                TokenHash = tokenHash,
                Scopes = "jobs:create jobs:read jobs:approve jobs:cancel",
                AuthMethod = "PairingCode",
                ClientIp = clientIp,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                ExpiresAt = tokenExpiresAt.ToString("O"),
                LastUsedAt = DateTime.UtcNow.ToString("O"),
                LastExtendedAt = DateTime.UtcNow.ToString("O"),
                RefreshEligibleAt = DateTime.UtcNow.Add(SlidingWindow).ToString("O")
            };

            db.AndroidAuthTokens.Add(authToken);
            await db.SaveChangesAsync();

            return (true, "Success", new TokenResponse
            {
                AccessToken = token,
                ExpiresAt = tokenExpiresAt,
                DeviceId = req.AppInstanceId,
                Scopes = new List<string> { "jobs:create", "jobs:read", "jobs:approve", "jobs:cancel" }
            });
        }

        public static async Task<(bool Success, string Message, TokenResponse? Token)> AuthorizeLanAsync(string deviceName, string appInstanceId, string clientIpAddress)
        {
            if (!IPAddress.TryParse(clientIpAddress, out var ipAddress))
            {
                return (false, "Invalid IP address.", null);
            }

            if (!IsPrivateIp(ipAddress) && !IsInSameSubnet(ipAddress))
            {
                return (false, "LAN authorization is only allowed for private or same-subnet IP addresses.", null);
            }

            bool approved = PromptApprovalWithTimeout(deviceName, clientIpAddress, TimeSpan.FromSeconds(10));
            if (!approved)
            {
                return (false, "LAN authorization request denied or timed out.", null);
            }

            // Generate token
            string token = "at_" + Guid.NewGuid().ToString("N");
            string tokenHash = Cryptography.ComputeHmacSha256(token);
            DateTime tokenExpiresAt = DateTime.UtcNow.Add(TokenLifetime);

            using var db = new AuthDatabase();
            

            var authToken = new AndroidAuthToken
            {
                TokenId = "tok_" + Guid.NewGuid().ToString("N"),
                DeviceName = deviceName,
                AppInstanceId = appInstanceId,
                TokenHash = tokenHash,
                Scopes = "jobs:create jobs:read jobs:approve jobs:cancel",
                AuthMethod = "LAN",
                ClientIp = clientIpAddress,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                ExpiresAt = tokenExpiresAt.ToString("O"),
                LastUsedAt = DateTime.UtcNow.ToString("O"),
                LastExtendedAt = DateTime.UtcNow.ToString("O"),
                RefreshEligibleAt = DateTime.UtcNow.Add(SlidingWindow).ToString("O")
            };

            db.AndroidAuthTokens.Add(authToken);
            await db.SaveChangesAsync();

            return (true, "Success", new TokenResponse
            {
                AccessToken = token,
                ExpiresAt = tokenExpiresAt,
                DeviceId = appInstanceId,
                Scopes = new List<string> { "jobs:create", "jobs:read", "jobs:approve", "jobs:cancel" }
            });
        }

        public static async Task<bool> ValidateTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            string tokenHash = Cryptography.ComputeHmacSha256(token);

            using var db = new AuthDatabase();
            

            var authToken = await db.AndroidAuthTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
            if (authToken == null) return false;

            if (DateTime.TryParse(authToken.ExpiresAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
            {
                if (expiresAt.ToUniversalTime() > DateTime.UtcNow)
                {
                    authToken.LastUsedAt = DateTime.UtcNow.ToString("O");
                    if (DateTime.TryParse(authToken.RefreshEligibleAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var refreshEligibleAt))
                    {
                        if (DateTime.UtcNow > refreshEligibleAt.ToUniversalTime())
                        {
                            authToken.ExpiresAt = DateTime.UtcNow.Add(TokenLifetime).ToString("O");
                            authToken.LastExtendedAt = DateTime.UtcNow.ToString("O");
                            authToken.RefreshEligibleAt = DateTime.UtcNow.Add(SlidingWindow).ToString("O");
                        }
                    }
                    await db.SaveChangesAsync();
                    return true;
                }
            }

            return false;
        }

        public static bool IsPrivateIp(IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = ip.GetAddressBytes();
                if (bytes[0] == 10) return true;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                if (bytes[0] == 127) return true;
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || IPAddress.IsLoopback(ip) || ip.IsIPv6SiteLocal) return true;
                byte[] bytes = ip.GetAddressBytes();
                if ((bytes[0] & 0xFE) == 0xFC) return true;
            }
            return false;
        }

        public static bool IsInSameSubnet(IPAddress clientIp)
        {
            if (IPAddress.IsLoopback(clientIp)) return true;

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;

                var ipProps = ni.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    var serverIp = unicast.Address;
                    if (serverIp.AddressFamily != clientIp.AddressFamily) continue;

                    if (clientIp.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var mask = unicast.IPv4Mask;
                        if (mask == null) continue;

                        if (IsInSameSubnetIPv4(clientIp, serverIp, mask)) return true;
                    }
                    else if (clientIp.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        int prefixLength = unicast.PrefixLength;
                        if (IsInSameSubnetIPv6(clientIp, serverIp, prefixLength)) return true;
                    }
                }
            }
            return false;
        }

        private static bool IsInSameSubnetIPv4(IPAddress ip1, IPAddress ip2, IPAddress mask)
        {
            byte[] ip1Bytes = ip1.GetAddressBytes();
            byte[] ip2Bytes = ip2.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();

            for (int i = 0; i < 4; i++)
            {
                if ((ip1Bytes[i] & maskBytes[i]) != (ip2Bytes[i] & maskBytes[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsInSameSubnetIPv6(IPAddress ip1, IPAddress ip2, int prefixLength)
        {
            byte[] ip1Bytes = ip1.GetAddressBytes();
            byte[] ip2Bytes = ip2.GetAddressBytes();

            int bytesToCompare = prefixLength / 8;
            int bitsToCompare = prefixLength % 8;

            for (int i = 0; i < bytesToCompare; i++)
            {
                if (ip1Bytes[i] != ip2Bytes[i]) return false;
            }

            if (bitsToCompare > 0 && bytesToCompare < 16)
            {
                byte mask = (byte)(0xFF << (8 - bitsToCompare));
                if ((ip1Bytes[bytesToCompare] & mask) != (ip2Bytes[bytesToCompare] & mask)) return false;
            }

            return true;
        }

        private static bool PromptApprovalWithTimeout(string deviceName, string ipAddress, TimeSpan timeout)
        {
            Console.WriteLine("[Android LAN Auth]");
            Console.WriteLine($"Device requested access: {deviceName}");
            Console.WriteLine($"Client IP: {ipAddress}");
            Console.WriteLine("Same network: yes");
            Console.Write("Approve this device? [Y/N] (10 seconds): ");

            bool useKeyAvailable = false;
            try
            {
                useKeyAvailable = !Console.IsInputRedirected;
            }
            catch { }

            if (useKeyAvailable)
            {
                var start = DateTime.UtcNow;
                while (DateTime.UtcNow - start < timeout)
                {
                    if (Console.KeyAvailable)
                    {
                        var keyInfo = Console.ReadKey(intercept: true);
                        if (keyInfo.KeyChar == 'y' || keyInfo.KeyChar == 'Y')
                        {
                            Console.WriteLine("Y");
                            return true;
                        }
                        if (keyInfo.KeyChar == 'n' || keyInfo.KeyChar == 'N')
                        {
                            Console.WriteLine("N");
                            return false;
                        }
                    }
                    Thread.Sleep(50);
                }
                Console.WriteLine("\nTimeout expired.");
                return false;
            }
            else
            {
                var readTask = Task.Run(() => Console.ReadLine());
                if (readTask.Wait(timeout))
                {
                    var line = readTask.Result?.Trim();
                    return string.Equals(line, "Y", StringComparison.OrdinalIgnoreCase);
                }
                return false;
            }
        }
    }

    public class TokenResponse
    {
        public string AccessToken { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public string DeviceId { get; set; } = "";
        public List<string> Scopes { get; set; } = new List<string>();
    }
}
