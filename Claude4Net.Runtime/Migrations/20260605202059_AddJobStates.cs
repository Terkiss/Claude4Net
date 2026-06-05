using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Claude4Net.Runtime.Migrations
{
    /// <inheritdoc />
    public partial class AddJobStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_states",
                columns: table => new
                {
                    JobId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Progress = table.Column<double>(type: "REAL", nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LatestMessage = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    PendingApproval = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChangedFiles = table.Column<string>(type: "TEXT", nullable: false),
                    VerificationState = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_states", x => x.JobId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_states");
        }
    }
}
