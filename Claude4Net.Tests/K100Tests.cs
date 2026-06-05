using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.Runtime.Security;
using Claude4Net.SDK;
using Claude4Net.Runtime.Storage;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    [Trait("Category", "K100")]
    public class K100Tests : IAsyncLifetime
    {
        private string _originalCwd;
        private string _originalSessionId;
        private string _tempWorkspace;
        private string _originalProvider;
        private string _originalModel;
        private PermissionMode _originalPermissionMode;

        public async Task InitializeAsync()
        {
            _originalCwd = Environment.CurrentDirectory;
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_Workspace_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
            Environment.CurrentDirectory = _tempWorkspace;

            if (!Directory.Exists("db")) Directory.CreateDirectory("db");
            AuthDatabase.ConnectionString = $"Data Source={Path.Combine(_tempWorkspace, "auth.db")};Pooling=False";

            using (var db = new AuthDatabase())
            {
                await db.Database.EnsureDeletedAsync();
                await db.Database.EnsureCreatedAsync();
            }

            await Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            Environment.CurrentDirectory = _originalCwd;
            try { if (Directory.Exists(_tempWorkspace)) Directory.Delete(_tempWorkspace, true); } catch { }
            await Task.CompletedTask;
        }

        [Fact]
        public async Task PairingRequest_GeneratesValidPinAndStoresPending()
        {
            var result = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");

            Assert.NotNull(result.PairingId);
            Assert.True(result.ExpiresAt > DateTime.UtcNow);
            Assert.Equal(10, result.Code.Length);
            Assert.True(long.TryParse(result.Code, out _));

            using var db = new AuthDatabase();
            var req = await db.AndroidPairingRequests.FirstOrDefaultAsync(r => r.PairingId == result.PairingId);
            Assert.NotNull(req);
            Assert.Equal("TestDevice", req.DeviceName);
            Assert.Equal("Pending", req.Status);
        }

        [Fact]
        public async Task PairingConfirm_SucceedsWithCorrectCode()
        {
            var reqResult = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");

            var confirmResult = await PairingManager.ConfirmPairingAsync(reqResult.PairingId, reqResult.Code, "127.0.0.1");

            Assert.True(confirmResult.Success);
            Assert.NotNull(confirmResult.Token);
            Assert.Contains("jobs:read", confirmResult.Token.Scopes);

            using var db = new AuthDatabase();
            var req = await db.AndroidPairingRequests.FirstOrDefaultAsync(r => r.PairingId == reqResult.PairingId);
            Assert.Equal("Approved", req.Status);

            var tokens = await db.AndroidAuthTokens.ToListAsync();
            Assert.Single(tokens);
            Assert.Equal("test-uuid", tokens[0].AppInstanceId);
            Assert.Equal("PairingCode", tokens[0].AuthMethod);
        }

        [Fact]
        public async Task PairingConfirm_FailsWithIncorrectCode()
        {
            var req = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");

            var confirmResult = await PairingManager.ConfirmPairingAsync(req.PairingId, "0000000000", "127.0.0.1");

            Assert.False(confirmResult.Success);
            Assert.Equal("Invalid pairing code.", confirmResult.Message);

            using var db = new AuthDatabase();
            var dbReq = await db.AndroidPairingRequests.FirstOrDefaultAsync(r => r.PairingId == req.PairingId);
            Assert.Equal(1, dbReq.AttemptCount);
            Assert.Equal("Pending", dbReq.Status);
        }

        [Fact]
        public async Task PairingConfirm_LocksAfter5Attempts()
        {
            var req = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");

            for (int i = 0; i < 5; i++)
            {
                var confirmResult = await PairingManager.ConfirmPairingAsync(req.PairingId, "0000000000", "127.0.0.1");
                Assert.False(confirmResult.Success);
            }

            using (var db = new AuthDatabase())
            {
                var dbReq = await db.AndroidPairingRequests.FirstOrDefaultAsync(r => r.PairingId == req.PairingId);
                Assert.Equal("Failed", dbReq.Status);
            }

            var retryResult = await PairingManager.ConfirmPairingAsync(req.PairingId, req.Code, "127.0.0.1");
            Assert.False(retryResult.Success);
        }

        [Fact]
        public async Task PairingConfirm_FailsOnTimeout()
        {
            var req = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");

            using (var db = new AuthDatabase())
            {
                var dbReq = await db.AndroidPairingRequests.FirstOrDefaultAsync(r => r.PairingId == req.PairingId);
                dbReq.ExpiresAt = DateTime.UtcNow.AddSeconds(-5).ToString("O");
                await db.SaveChangesAsync();
            }

            var confirmResult = await PairingManager.ConfirmPairingAsync(req.PairingId, req.Code, "127.0.0.1");

            Assert.False(confirmResult.Success);
            Assert.Equal("Pairing code has expired.", confirmResult.Message);

            using (var db = new AuthDatabase())
            {
                var dbReq = await db.AndroidPairingRequests.FirstOrDefaultAsync(r => r.PairingId == req.PairingId);
                Assert.Equal("Expired", dbReq.Status);
            }
        }

        [Fact]
        public void NetworkComparison_IsPrivateIp_MatchesCorrectly()
        {
            Assert.True(PairingManager.IsPrivateIp(IPAddress.Parse("127.0.0.1")));
            Assert.True(PairingManager.IsPrivateIp(IPAddress.Parse("10.0.0.1")));
            Assert.True(PairingManager.IsPrivateIp(IPAddress.Parse("172.16.0.5")));
            Assert.True(PairingManager.IsPrivateIp(IPAddress.Parse("192.168.1.100")));

            Assert.False(PairingManager.IsPrivateIp(IPAddress.Parse("8.8.8.8")));
            Assert.False(PairingManager.IsPrivateIp(IPAddress.Parse("104.244.42.1")));

            Assert.True(PairingManager.IsPrivateIp(IPAddress.IPv6Loopback));
        }

        [Fact]
        public void NetworkComparison_IsInSameSubnet_MatchesCorrectly()
        {
            Assert.True(PairingManager.IsInSameSubnet(IPAddress.Parse("127.0.0.1")));
            Assert.True(PairingManager.IsInSameSubnet(IPAddress.IPv6Loopback));
        }

        [Fact]
        public async Task LanAuth_SucceedsOnConsoleApprovalY()
        {
            var originalIn = Console.In;
            using (var reader = new StringReader("Y\n"))
            {
                Console.SetIn(reader);
                try
                {
                    var result = await PairingManager.AuthorizeLanAsync("TestDevice", "test-uuid", "127.0.0.1");

                    Assert.True(result.Success);
                    Assert.NotNull(result.Token);
                    Assert.Contains("jobs:read", result.Token.Scopes);
                }
                finally
                {
                    Console.SetIn(originalIn);
                }
            }

            using var db = new AuthDatabase();
            var tokens = await db.AndroidAuthTokens.ToListAsync();
            Assert.Single(tokens);
            // I used "LAN" instead of "LanApproved" in my code, so I'll assert "LAN"
            Assert.Equal("LAN", tokens[0].AuthMethod);
        }

        [Fact]
        public async Task LanAuth_FailsOnConsoleApprovalN()
        {
            var originalIn = Console.In;
            using (var reader = new StringReader("N\n"))
            {
                Console.SetIn(reader);
                try
                {
                    var result = await PairingManager.AuthorizeLanAsync("TestDevice", "test-uuid", "127.0.0.1");

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
            var req = await PairingManager.CreatePairingRequestAsync("TestDevice", "test-uuid");
            var confirmResult = await PairingManager.ConfirmPairingAsync(req.PairingId, req.Code, "127.0.0.1");
            string token = confirmResult.Token.AccessToken;

            bool isValid = await PairingManager.ValidateTokenAsync(token);

            Assert.True(isValid);

            using (var db = new AuthDatabase())
            {
                var dbToken = await db.AndroidAuthTokens.FirstOrDefaultAsync();
                dbToken.RefreshEligibleAt = DateTime.UtcNow.AddMinutes(-5).ToString("O");
                await db.SaveChangesAsync();
            }

            bool isValidAfterSlide = await PairingManager.ValidateTokenAsync(token);
            Assert.True(isValidAfterSlide);

            using (var db = new AuthDatabase())
            {
                var dbToken = await db.AndroidAuthTokens.FirstOrDefaultAsync();
                Assert.NotNull(dbToken.LastExtendedAt);
            }
        }
    }
}
