using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchTrigramIndexes : Migration
    {
        // pg_trgm GIN indexes make leading-wildcard ILIKE ('%term%') index-usable, which
        // the existing btree indexes cannot serve. Backs the database-search sections
        // (source / character / item-catalog name matching). Raw SQL because these are an
        // extension + operator-class indexes EF's fluent API can't express.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_LootRecords_SourceName_trgm"
                ON "LootRecords" USING gin ("SourceName" gin_trgm_ops);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_GameCharacters_DisplayName_trgm"
                ON "GameCharacters" USING gin ("DisplayName" gin_trgm_ops);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_GearItems_Name_trgm"
                ON "GearItems" USING gin ("Name" gin_trgm_ops);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_CollectionLogItems_Name_trgm"
                ON "CollectionLogItems" USING gin ("Name" gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CollectionLogItems_Name_trgm\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_GearItems_Name_trgm\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_GameCharacters_DisplayName_trgm\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_LootRecords_SourceName_trgm\";");
            // Leave the pg_trgm extension installed — other objects may depend on it.
        }
    }
}
