using System;
using System.IO;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Claude4Net.Runtime.Storage
{
    public class AuthDatabase : DbContext
    {
        public DbSet<AndroidPairingRequest> AndroidPairingRequests { get; set; } = null!;
        public DbSet<AndroidAuthToken> AndroidAuthTokens { get; set; } = null!;

        public static string ConnectionString { get; set; } = "Data Source=db/auth.db;Pooling=False";

        public AuthDatabase() { }
        public AuthDatabase(DbContextOptions<AuthDatabase> options) : base(options) { }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                if (ConnectionString.Contains("db/auth.db") && !Directory.Exists("db")) Directory.CreateDirectory("db");
                optionsBuilder.UseSqlite(ConnectionString);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AndroidPairingRequest>().ToTable("android_pairing_requests");
            modelBuilder.Entity<AndroidAuthToken>().ToTable("android_auth_tokens");
            base.OnModelCreating(modelBuilder);
        }
    }

    public class AndroidPairingRequest
    {
        [Key]
        [MaxLength(64)]
        public string PairingId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string DeviceName { get; set; } = string.Empty;

        [MaxLength(128)]
        public string AppInstanceId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string CodeHash { get; set; } = string.Empty;

        [MaxLength(64)]
        public string CreatedAt { get; set; } = string.Empty;

        [MaxLength(64)]
        public string ExpiresAt { get; set; } = string.Empty;

        public int AttemptCount { get; set; }

        [MaxLength(32)]
        public string Status { get; set; } = string.Empty;
    }

    public class AndroidAuthToken
    {
        [Key]
        [MaxLength(64)]
        public string TokenId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string DeviceName { get; set; } = string.Empty;

        [MaxLength(128)]
        public string AppInstanceId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string TokenHash { get; set; } = string.Empty;

        [MaxLength(256)]
        public string Scopes { get; set; } = string.Empty;

        [MaxLength(32)]
        public string AuthMethod { get; set; } = string.Empty;

        [MaxLength(64)]
        public string ClientIp { get; set; } = string.Empty;

        [MaxLength(64)]
        public string CreatedAt { get; set; } = string.Empty;

        [MaxLength(64)]
        public string ExpiresAt { get; set; } = string.Empty;

        [MaxLength(64)]
        public string LastUsedAt { get; set; } = string.Empty;

        [MaxLength(64)]
        public string LastExtendedAt { get; set; } = string.Empty;

        [MaxLength(64)]
        public string RefreshEligibleAt { get; set; } = string.Empty;
    }

    public class AuthDatabaseContextFactory : IDesignTimeDbContextFactory<AuthDatabase>
    {
        public AuthDatabase CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AuthDatabase>();
            if (!Directory.Exists("db")) Directory.CreateDirectory("db");
            optionsBuilder.UseSqlite("Data Source=db/auth.db;Pooling=False");

            return new AuthDatabase(optionsBuilder.Options);
        }
    }
}
