using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime.Storage;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    [Trait("Category", "K099")]
    public class K099Tests
    {
        [Fact]
        public async Task K099_CanCreateAndRetrievePairingRequest()
        {
            var options = new DbContextOptionsBuilder<AuthDatabase>()
                .UseSqlite("Data Source=test_auth.db")
                .Options;

            using (var db = new AuthDatabase(options))
            {
                await db.Database.EnsureDeletedAsync();
                await db.Database.EnsureCreatedAsync();

                var req = new AndroidPairingRequest
                {
                    PairingId = "pair_test1",
                    DeviceName = "Test Device",
                    AppInstanceId = "app1",
                    CodeHash = "hash1",
                    CreatedAt = DateTime.UtcNow.ToString("O"),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(5).ToString("O"),
                    AttemptCount = 0,
                    Status = "Pending"
                };

                db.AndroidPairingRequests.Add(req);
                await db.SaveChangesAsync();
            }

            using (var db = new AuthDatabase(options))
            {
                var req = await db.AndroidPairingRequests.FirstOrDefaultAsync(r => r.PairingId == "pair_test1");
                Assert.NotNull(req);
                Assert.Equal("Test Device", req.DeviceName);
                Assert.Equal("Pending", req.Status);
                
                req.Status = "Approved";
                await db.SaveChangesAsync();
            }

            using (var db = new AuthDatabase(options))
            {
                var req = await db.AndroidPairingRequests.FirstOrDefaultAsync(r => r.PairingId == "pair_test1");
                Assert.Equal("Approved", req.Status);
            }
        }

        [Fact]
        public async Task K099_CanCreateAndRetrieveAuthToken()
        {
            var options = new DbContextOptionsBuilder<AuthDatabase>()
                .UseSqlite("Data Source=test_auth.db")
                .Options;

            using (var db = new AuthDatabase(options))
            {
                await db.Database.EnsureDeletedAsync();
                await db.Database.EnsureCreatedAsync();

                var token = new AndroidAuthToken
                {
                    TokenId = "tok_test1",
                    DeviceName = "Test Device",
                    AppInstanceId = "app1",
                    TokenHash = "hash_t1",
                    Scopes = "jobs:read",
                    AuthMethod = "PairingCode",
                    ClientIp = "127.0.0.1",
                    CreatedAt = DateTime.UtcNow.ToString("O"),
                    ExpiresAt = DateTime.UtcNow.AddDays(3).ToString("O"),
                    LastUsedAt = DateTime.UtcNow.ToString("O"),
                    LastExtendedAt = DateTime.UtcNow.ToString("O"),
                    RefreshEligibleAt = DateTime.UtcNow.AddDays(1).ToString("O")
                };

                db.AndroidAuthTokens.Add(token);
                await db.SaveChangesAsync();
            }

            using (var db = new AuthDatabase(options))
            {
                var token = await db.AndroidAuthTokens.FirstOrDefaultAsync(t => t.TokenId == "tok_test1");
                Assert.NotNull(token);
                Assert.Equal("Test Device", token.DeviceName);
                
                token.LastUsedAt = DateTime.UtcNow.ToString("O");
                await db.SaveChangesAsync();
            }
        }
    }
}
