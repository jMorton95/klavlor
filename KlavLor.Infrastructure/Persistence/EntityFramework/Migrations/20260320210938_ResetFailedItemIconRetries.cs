using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class ResetFailedItemIconRetries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-time reset: allow the backfill to re-attempt all unresolved icons
            // after switching from manual URL construction to the imageinfo API.
            migrationBuilder.Sql("""
                UPDATE "ItemIcons"
                SET "FailCount" = 0,
                    "LastAttemptAt" = NULL
                WHERE "CachedImageId" IS NULL
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No meaningful rollback — the backfill will re-populate these values.
        }
    }
}
