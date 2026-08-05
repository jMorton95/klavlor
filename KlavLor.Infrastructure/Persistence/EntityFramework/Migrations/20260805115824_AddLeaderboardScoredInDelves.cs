using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaderboardScoredInDelves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ScoredInDelves",
                table: "LuckLeaderboardEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScoredInDelves",
                table: "LuckLeaderboardEntries");
        }
    }
}
