using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddFirstTimeFlagToDrops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill IsFirstTime=true on the earliest (by OccurredAt) drop of each
            // unique item name per GameCharacter. Done by rebuilding DropsJson for
            // every affected record using jsonb_agg + a DISTINCT ON CTE that picks
            // the earliest (record, item) combination. Records with NULL
            // GameCharacterId are skipped (never shown on character-scoped UI).
            migrationBuilder.Sql("""
                WITH unrolled AS (
                    SELECT lr."Id" AS rec_id, lr."GameCharacterId" AS cid,
                           lr."OccurredAt" AS t, d.elem->>'Name' AS item_name, d.idx
                    FROM "LootRecords" lr,
                         jsonb_array_elements(lr."DropsJson") WITH ORDINALITY AS d(elem, idx)
                    WHERE lr."GameCharacterId" IS NOT NULL
                ),
                firsts AS (
                    SELECT DISTINCT ON (cid, item_name) rec_id, item_name
                    FROM unrolled
                    ORDER BY cid, item_name, t, rec_id, idx
                )
                UPDATE "LootRecords" lr
                SET "DropsJson" = (
                    SELECT jsonb_agg(
                        CASE
                            WHEN EXISTS (SELECT 1 FROM firsts f
                                         WHERE f.rec_id = lr."Id"
                                           AND f.item_name = d.elem->>'Name')
                            THEN d.elem || '{"IsFirstTime": true}'::jsonb
                            ELSE d.elem
                        END
                        ORDER BY d.idx
                    )
                    FROM jsonb_array_elements(lr."DropsJson") WITH ORDINALITY AS d(elem, idx)
                )
                WHERE lr."Id" IN (SELECT DISTINCT rec_id FROM firsts);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No meaningful rollback — IsFirstTime is a denormalized cache and can
            // be re-derived by re-running the Up SQL.
        }
    }
}
