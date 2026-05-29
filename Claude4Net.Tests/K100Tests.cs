using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.Runtime.Security;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    [Trait("Category", "K100")]
    public class K100Tests : IAsyncLifetime
    {
        public async Task InitializeAsync()
        {
            await PandasUniverseManager.Instance.ResetAndFlushForTestAsync();
        }

        public async Task DisposeAsync()
        {
            await PandasUniverseManager.Instance.ResetAndFlushForTestAsync();
        }

        [Fact]
        public async Task PairingRequest_GeneratesValidPinAndStoresPending()
        {
            // Act
            var result = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");

            // Assert
            Assert.NotNull(result.PairingId);
            Assert.StartsWith("pair_", result.PairingId);
            Assert.Equal(10, result.Code.Length);
            Assert.True(long.TryParse(result.Code, out _));
            Assert.True(result.ExpiresAt > DateTime.UtcNow);

            // Verify entry in DB
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("android_pairing_requests");
                Assert.Equal(1, df.RowCount);
                Assert.Equal(result.PairingId, df[0, "PairingId"]?.ToString());
                Assert.Equal("Pending", df[0, "Status"]?.ToString());
                Assert.Equal("0", df[0, "AttemptCount"]?.ToString());
            });
        }

        [Fact]
        public async Task PairingConfirm_SucceedsWithCorrectCode()
        {
            // Arrange
            var req = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");

            // Act
            var confirmResult = await PairingManager.ConfirmPairingAsync(req.PairingId, req.Code, "127.0.0.1");

            // Assert
            Assert.True(confirmResult.Success);
            Assert.NotNull(confirmResult.Token);
            Assert.StartsWith("c4n_at_", confirmResult.Token.AccessToken);
            Assert.Equal("test-uuid", confirmResult.Token.DeviceId);
            Assert.Contains("jobs:read", confirmResult.Token.Scopes);

            // Verify status updated in DB
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("android_pairing_requests");
                Assert.Equal("Confirmed", df[0, "Status"]?.ToString());

                // Verify token created
                var tokensDf = u.GetTableOrThrow("android_auth_tokens");
                Assert.Equal(1, tokensDf.RowCount);
                Assert.Equal("test-uuid", tokensDf[0, "AppInstanceId"]?.ToString());
                Assert.Equal("PairingCode", tokensDf[0, "AuthMethod"]?.ToString());
            });
        }

        [Fact]
        public async Task PairingConfirm_FailsWithIncorrectCode()
        {
            // Arrange
            var req = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");

            // Act
            var confirmResult = await PairingManager.ConfirmPairingAsync(req.PairingId, "0000000000", "127.0.0.1");

            // Assert
            Assert.False(confirmResult.Success);
            Assert.Equal("Invalid pairing code.", confirmResult.Message);

            // Verify attempt count incremented
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("android_pairing_requests");
                Assert.Equal("1", df[0, "AttemptCount"]?.ToString());
                Assert.Equal("Pending", df[0, "Status"]?.ToString());
            });
        }

        [Fact]
        public async Task PairingConfirm_LocksAfter5Attempts()
        {
            // Arrange
            var req = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");

            // Act
            for (int i = 0; i < 5; i++)
            {
                var confirmResult = await PairingManager.ConfirmPairingAsync(req.PairingId, "0000000000", "127.0.0.1");
                Assert.False(confirmResult.Success);
            }

            // Assert
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("android_pairing_requests");
                Assert.Equal("5", df[0, "AttemptCount"]?.ToString());
                Assert.Equal("Failed", df[0, "Status"]?.ToString());
            });

            // Subsequent confirm should fail because status is Failed
            var retryResult = await PairingManager.ConfirmPairingAsync(req.PairingId, req.Code, "127.0.0.1");
            Assert.False(retryResult.Success);
            Assert.Contains("Failed", retryResult.Message);
        }

        [Fact]
        public async Task PairingConfirm_FailsOnTimeout()
        {
            // Arrange
            var req = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");

            // Manually expire the request in DB
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("android_pairing_requests");
                df[0, "ExpiresAt"] = DateTime.UtcNow.AddSeconds(-5).ToString("O");
                u.AddOrUpdateTable("android_pairing_requests", df);
            });

            // Act
            var confirmResult = await PairingManager.ConfirmPairingAsync(req.PairingId, req.Code, "127.0.0.1");

            // Assert
            Assert.False(confirmResult.Success);
            Assert.Equal("Pairing code has expired.", confirmResult.Message);

            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("android_pairing_requests");
                Assert.Equal("Expired", df[0, "Status"]?.ToString());
            });
        }

        [Fact]
        public void NetworkComparison_IsPrivateIp_MatchesCorrectly()
        {
            // Private IPv4
            Assert.True(PairingManager.IsPrivateIp(IPAddress.Parse("127.0.0.1")));
            Assert.True(PairingManager.IsPrivateIp(IPAddress.Parse("10.0.0.1")));
            Assert.True(PairingManager.IsPrivateIp(IPAddress.Parse("172.16.0.5")));
            Assert.True(PairingManager.IsPrivateIp(IPAddress.Parse("192.168.1.100")));

            // Public IPv4
            Assert.False(PairingManager.IsPrivateIp(IPAddress.Parse("8.8.8.8")));
            Assert.False(PairingManager.IsPrivateIp(IPAddress.Parse("104.244.42.1")));

            // IPv6
            Assert.True(PairingManager.IsPrivateIp(IPAddress.IPv6Loopback));
        }

        [Fact]
        public void NetworkComparison_IsInSameSubnet_MatchesCorrectly()
        {
            // Loopback is always in same subnet
            Assert.True(PairingManager.IsInSameSubnet(IPAddress.Parse("127.0.0.1")));
            Assert.True(PairingManager.IsInSameSubnet(IPAddress.IPv6Loopback));
        }

        [Fact]
        public async Task LanAuth_SucceedsOnConsoleApprovalY()
        {
            // Arrange: mock Console.In using a StringReader writing "Y\n"
            var originalIn = Console.In;
            using (var reader = new StringReader("Y\n"))
            {
                Console.SetIn(reader);
                try
                {
                    // Act
                    var result = await PairingManager.AuthorizeLanAsync("TestDevice", "test-uuid", "127.0.0.1");

                    // Assert
                    Assert.True(result.Success);
                    Assert.NotNull(result.Token);
                    Assert.Contains("jobs:read", result.Token.Scopes);
                }
                finally
                {
                    Console.SetIn(originalIn);
                }
            }

            // Verify token in DB
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var tokensDf = u.GetTableOrThrow("android_auth_tokens");
                Assert.Equal(1, tokensDf.RowCount);
                Assert.Equal("LanApproved", tokensDf[0, "AuthMethod"]?.ToString());
            });
        }

        [Fact]
        public async Task LanAuth_FailsOnConsoleApprovalN()
        {
            // Arrange: mock Console.In using a StringReader writing "N\n"
            var originalIn = Console.In;
            using (var reader = new StringReader("N\n"))
            {
                Console.SetIn(reader);
                try
                {
                    // Act
                    var result = await PairingManager.AuthorizeLanAsync("TestDevice", "test-uuid", "127.0.0.1");

                    // Assert
                    Assert.False(result.Success);
                    Assert.Null(result.Token);
                }
                finally
                {
                    Console.SetIn(originalIn);
                }
            }
        }

        [Fact]
        public async Task TokenValidation_ValidatesAndSlidesExpiration()
        {
            // Arrange
            var req = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");
            var confirmResult = await PairingManager.ConfirmPairingAsync(req.PairingId, req.Code, "127.0.0.1");
            string token = confirmResult.Token.AccessToken;

            // Act
            bool isValid = await PairingManager.ValidateTokenAsync(token);

            // Assert
            Assert.True(isValid);

            // Manually set RefreshEligibleAt to simulate past sliding window
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("android_auth_tokens");
                df[0, "RefreshEligibleAt"] = DateTime.UtcNow.AddMinutes(-5).ToString("O");
                u.AddOrUpdateTable("android_auth_tokens", df);
            });

            // Act: Validate again, this should trigger sliding expiration extension
            bool isValidAfterSlide = await PairingManager.ValidateTokenAsync(token);
            Assert.True(isValidAfterSlide);

            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("android_auth_tokens");
                Assert.NotEmpty(df[0, "LastExtendedAt"]?.ToString() ?? "");
            });
        }
    }
}
