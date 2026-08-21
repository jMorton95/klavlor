using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddLootRecordLuckExclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExcludedFromLuck",
                table: "LootRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_LootRecords_ExcludedFromLuck",
                table: "LootRecords",
                column: "ExcludedFromLuck",
                filter: "\"ExcludedFromLuck\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LootRecords_ExcludedFromLuck",
                table: "LootRecords");

            migrationBuilder.DropColumn(
                name: "ExcludedFromLuck",
                table: "LootRecords");
        }
    }
}
