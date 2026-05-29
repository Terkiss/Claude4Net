using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;

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
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("android_pairing_requests");
                var newRowCols = new Dictionary<string, IColumn>
                {
                    ["PairingId"] = new StringColumn(new[] { pairingId }),
                    ["DeviceName"] = new StringColumn(new[] { deviceName }),
                    ["AppInstanceId"] = new StringColumn(new[] { appInstanceId }),
                    ["CodeHash"] = new StringColumn(new[] { codeHash }),
                    ["CreatedAt"] = new StringColumn(new[] { DateTime.UtcNow.ToString("O") }),
                    ["ExpiresAt"] = new StringColumn(new[] { expiresAt.ToString("O") }),
                    ["AttemptCount"] = new PrimitiveColumn<int>(new[] { 0 }),
                    ["Status"] = new StringColumn(new[] { "Pending" })
                };
                var newRowDf = new DataFrame(newRowCols);
                var updatedDf = DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                u.AddOrUpdateTable("android_pairing_requests", updatedDf);
            });

            // 5. Print to console
            Console.WriteLine("[Android Pairing]");
            Console.WriteLine($"Device requested access: {deviceName}");
            Console.WriteLine($"Pairing code: {pin}");
            Console.WriteLine("Expires in: 30 seconds");

            return (pairingId, expiresAt, pin);
        }

        public static async Task<(bool Success, string Message, TokenResponse? Token)> ConfirmPairingAsync(string pairingId, string code, string clientIp)
        {
            return await PandasUniverseManager.Instance.ExecuteAsync(async u =>
            {
                var df = u.GetTableOrThrow("android_pairing_requests");
                int targetRow = -1;

                for (int i = 0; i < df.RowCount; i++)
                {
                    if (df[i, "PairingId"]?.ToString() == pairingId)
                    {
                        targetRow = i;
                        break;
                    }
                }

                if (targetRow == -1)
                {
                    return (false, "Pairing request not found.", null);
                }

                string status = df[targetRow, "Status"]?.ToString() ?? "";
                if (status != "Pending")
                {
                    return (false, $"Pairing request is already {status}.", null);
                }

                if (DateTime.TryParse(df[targetRow, "ExpiresAt"]?.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
                {
                    if (DateTime.UtcNow > expiresAt.ToUniversalTime())
                    {
                        df[targetRow, "Status"] = "Expired";
                        u.AddOrUpdateTable("android_pairing_requests", df);
                        return (false, "Pairing code has expired.", null);
                    }
                }


                int attemptCount = 0;
                var rawAttempt = df[targetRow, "AttemptCount"];
                if (rawAttempt != null)
                {
                    int.TryParse(rawAttempt.ToString(), out attemptCount);
                }


                attemptCount++;
                df[targetRow, "AttemptCount"] = attemptCount;

                string codeHash = df[targetRow, "CodeHash"]?.ToString() ?? "";
                string inputHash = Cryptography.ComputeHmacSha256(code);

                if (codeHash != inputHash)
                {
                    if (attemptCount >= 5)
                    {
                        df[targetRow, "Status"] = "Failed";
                    }
                    u.AddOrUpdateTable("android_pairing_requests", df);
                    return (false, "Invalid pairing code.", null);
                }

                // Success!
                df[targetRow, "Status"] = "Confirmed";
                u.AddOrUpdateTable("android_pairing_requests", df);

                string deviceName = df[targetRow, "DeviceName"]?.ToString() ?? "";
                string appInstanceId = df[targetRow, "AppInstanceId"]?.ToString() ?? "";

                var token = await IssueTokenAsync(u, deviceName, appInstanceId, "PairingCode", clientIp);
                return (true, "Pairing confirmed successfully.", token);
            });
        }

        public static async Task<(bool Success, string Message, TokenResponse? Token)> AuthorizeLanAsync(string deviceName, string appInstanceId, string clientIpAddress)
        {
            if (!IPAddress.TryParse(clientIpAddress, out var ip))
            {
                return (false, "Invalid client IP address format.", null);
            }

            if (!IsPrivateIp(ip))
            {
                return (false, "Client IP is not in a private range.", null);
            }

            if (!IsInSameSubnet(ip))
            {
                return (false, "Client IP is not in the same subnet as the host.", null);
            }

            // Prompt console with 10-second timeout
            bool approved = PromptApprovalWithTimeout(deviceName, clientIpAddress, TimeSpan.FromSeconds(10));
            if (!approved)
            {
                return (false, "LAN authorization request was denied or timed out.", null);
            }

            var token = await PandasUniverseManager.Instance.ExecuteAsync(async u =>
            {
                return await IssueTokenAsync(u, deviceName, appInstanceId, "LanApproved", clientIpAddress);
            });

            return (true, "LAN authorization successful.", token);
        }

        private static async Task<TokenResponse> IssueTokenAsync(DataUniverse u, string deviceName, string appInstanceId, string authMethod, string clientIp)
        {
            byte[] tokenBytes = new byte[32];
            RandomNumberGenerator.Fill(tokenBytes);
            string token = "c4n_at_" + Convert.ToHexString(tokenBytes).ToLowerInvariant();
            string tokenHash = Cryptography.ComputeHmacSha256(token);

            string tokenId = "tok_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            DateTime expiresAt = DateTime.UtcNow.Add(TokenLifetime);
            DateTime refreshEligibleAt = DateTime.UtcNow.Add(SlidingWindow);

            var df = u.GetTableOrThrow("android_auth_tokens");
            var newRowCols = new Dictionary<string, IColumn>
            {
                ["TokenId"] = new StringColumn(new[] { tokenId }),
                ["DeviceName"] = new StringColumn(new[] { deviceName }),
                ["AppInstanceId"] = new StringColumn(new[] { appInstanceId }),
                ["TokenHash"] = new StringColumn(new[] { tokenHash }),
                ["Scopes"] = new StringColumn(new[] { "[\"jobs:create\",\"jobs:read\",\"jobs:approve\",\"jobs:cancel\"]" }),
                ["AuthMethod"] = new StringColumn(new[] { authMethod }),
                ["ClientIp"] = new StringColumn(new[] { clientIp }),
                ["CreatedAt"] = new StringColumn(new[] { DateTime.UtcNow.ToString("O") }),
                ["ExpiresAt"] = new StringColumn(new[] { expiresAt.ToString("O") }),
                ["LastUsedAt"] = new StringColumn(new[] { "" }),
                ["LastExtendedAt"] = new StringColumn(new[] { "" }),
                ["RefreshEligibleAt"] = new StringColumn(new[] { refreshEligibleAt.ToString("O") })
            };
            var newRowDf = new DataFrame(newRowCols);
            var updatedDf = DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
            u.AddOrUpdateTable("android_auth_tokens", updatedDf);

            return new TokenResponse
            {
                AccessToken = token,
                ExpiresAt = expiresAt,
                DeviceId = appInstanceId,
                Scopes = new List<string> { "jobs:create", "jobs:read", "jobs:approve", "jobs:cancel" }
            };
        }

        public static async Task<bool> ValidateTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            string tokenHash = Cryptography.ComputeHmacSha256(token);

            return await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("android_auth_tokens")) return false;
                var df = u.GetTableOrThrow("android_auth_tokens");
                for (int i = 0; i < df.RowCount; i++)
                {
                    if (df[i, "TokenHash"]?.ToString() == tokenHash)
                    {
                        if (DateTime.TryParse(df[i, "ExpiresAt"]?.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
                        {
                            if (expiresAt.ToUniversalTime() > DateTime.UtcNow)
                            {
                                df[i, "LastUsedAt"] = DateTime.UtcNow.ToString("O");
                                if (DateTime.TryParse(df[i, "RefreshEligibleAt"]?.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var refreshEligibleAt))
                                {
                                    if (DateTime.UtcNow > refreshEligibleAt.ToUniversalTime())
                                    {
                                        df[i, "ExpiresAt"] = DateTime.UtcNow.Add(TokenLifetime).ToString("O");
                                        df[i, "LastExtendedAt"] = DateTime.UtcNow.ToString("O");
                                        df[i, "RefreshEligibleAt"] = DateTime.UtcNow.Add(SlidingWindow).ToString("O");
                                    }
                                }
                                u.AddOrUpdateTable("android_auth_tokens", df);
                                return true;
                            }
                        }

                    }
                }
                return false;
            });
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
