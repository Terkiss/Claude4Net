using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Claude4Net.Runtime.Migrations
{
    /// <inheritdoc />
    public partial class AddAndroidAuthSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "android_auth_tokens",
                columns: table => new
                {
                    TokenId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AppInstanceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AuthMethod = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ClientIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastUsedAt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastExtendedAt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RefreshEligibleAt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_android_auth_tokens", x => x.TokenId);
                });

            migrationBuilder.CreateTable(
                name: "android_pairing_requests",
                columns: table => new
                {
                    PairingId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AppInstanceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_android_pairing_requests", x => x.PairingId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "android_auth_tokens");

            migrationBuilder.DropTable(
                name: "android_pairing_requests");
        }
    }
}
