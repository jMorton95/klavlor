using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialDropIndexForLegendaryFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LootDrops_LootRecordId_Special",
                table: "LootDrops",
                columns: new[] { "IsSpecial", "LootRecordId" },
                filter: "\"IsSpecial\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LootDrops_LootRecordId_Special",
                table: "LootDrops");
        }
    }
}
