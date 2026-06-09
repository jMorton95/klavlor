using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddLootFeedOrdinalIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LootRecords_GameCharacterId_SourceName_OccurredAt_Id",
                table: "LootRecords",
                columns: new[] { "GameCharacterId", "SourceName", "OccurredAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LootRecords_GameCharacterId_SourceName_OccurredAt_Id",
                table: "LootRecords");
        }
    }
}
