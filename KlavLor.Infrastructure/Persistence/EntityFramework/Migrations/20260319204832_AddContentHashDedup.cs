using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddContentHashDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "LootRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LootRecords_UserId_ContentHash",
                table: "LootRecords",
                columns: new[] { "UserId", "ContentHash" },
                unique: true,
                filter: "\"ContentHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LootRecords_UserId_ContentHash",
                table: "LootRecords");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "LootRecords");
        }
    }
}
