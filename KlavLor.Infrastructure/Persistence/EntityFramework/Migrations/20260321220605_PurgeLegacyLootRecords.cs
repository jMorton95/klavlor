using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class PurgeLegacyLootRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-time purge of all legacy loot data that predates the GameCharacter system.
            // New data will be re-synced with character IDs attached.
            migrationBuilder.Sql("""DELETE FROM "LootRecords";""");
            migrationBuilder.Sql("""DELETE FROM "GameCharacters";""");
            migrationBuilder.Sql("""DELETE FROM "SourceIcons";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
