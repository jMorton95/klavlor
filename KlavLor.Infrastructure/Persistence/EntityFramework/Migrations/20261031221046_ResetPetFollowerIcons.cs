using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class ResetPetFollowerIcons : Migration
    {
        // A pet's icon was cached from its wiki follower render rather than its inventory sprite
        // (see OsrsWikiClient.DeriveIconFilenames). The backfill never revisits an icon that has an
        // image, so the ones already cached are released here for it to resolve again. No item's
        // inventory sprite is named "follower"; the source-icon guard is belt and braces.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "ItemIcons"
                SET "CachedImageId" = NULL,
                    "FailCount" = 0,
                    "LastAttemptAt" = NULL
                WHERE "CachedImageId" IN (SELECT "Id" FROM "CachedImages" WHERE "SourceUrl" ILIKE '%follower%')
                """);

            migrationBuilder.Sql("""
                DELETE FROM "CachedImages" c
                WHERE c."SourceUrl" ILIKE '%follower%'
                  AND NOT EXISTS (SELECT 1 FROM "ItemIcons" i WHERE i."CachedImageId" = c."Id")
                  AND NOT EXISTS (SELECT 1 FROM "SourceIcons" s WHERE s."CachedImageId" = c."Id")
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No meaningful rollback: the backfill re-populates these icons.
        }
    }
}
