using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddLoweredSourceAndItemNameIndexes : Migration
    {
        // EXPRESSION INDEXES, hand-written because EF cannot model them.
        //
        // Names reaching this database come from three vocabularies that disagree on case - the
        // wiki's article titles, the wiki's own summary tables, and whatever RuneLite reports - so
        // every name match on the read side is `lower(x) = ANY(...)`. There are btree indexes on
        // "SourceName" and "Name" already, but a plain btree cannot serve a lower() predicate, so
        // those queries fell back to a sequential scan.
        //
        // Measured on the superiors page before this: GetCounts filtered 26,732 rows off a Seq Scan
        // of "LootRecords" and the receipts query filtered another 39,810 off "LootDrops", for
        // 143ms of query time on a table of ~29k records. Both scans grow with the table.
        //
        // NOT a replacement for the case-sensitive indexes: those still serve the many queries that
        // match a name exactly.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_LootRecords_SourceNameLower" ON "LootRecords" (lower("SourceName"))
                """);
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_LootDrops_NameLower" ON "LootDrops" (lower("Name"))
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_LootRecords_SourceNameLower"
                """);
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_LootDrops_NameLower"
                """);
        }
    }
}
