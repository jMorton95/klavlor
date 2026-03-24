using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddLootFeedCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LootRecords_GameCharacterId_TotalValue_OccurredAt",
                table: "LootRecords",
                columns: new[] { "GameCharacterId", "TotalValue", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LootRecords_GameCharacterId_TotalValue_OccurredAt",
                table: "LootRecords");
        }
    }
}
