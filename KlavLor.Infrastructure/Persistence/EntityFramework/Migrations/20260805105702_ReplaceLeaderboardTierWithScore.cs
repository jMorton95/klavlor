using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceLeaderboardTierWithScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LuckLeaderboardEntries_Generation_Board_Tier",
                table: "LuckLeaderboardEntries");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "LuckLeaderboardEntries");

            migrationBuilder.AddColumn<double>(
                name: "Score",
                table: "LuckLeaderboardEntries",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateIndex(
                name: "IX_LuckLeaderboardEntries_Generation_Board_Score",
                table: "LuckLeaderboardEntries",
                columns: new[] { "Generation", "Board", "Score" });

            // Existing rows would carry Score = 0 and so render in an arbitrary order until the
            // hourly refresh replaced them. The table is a pure cache — every row is rebuilt from
            // loot records each cycle — so clearing it is lossless, and an empty board for up to an
            // hour is a better failure mode than a visibly mis-ranked one. An admin can trigger the
            // refresh immediately from the job-health panel to skip the wait.
            migrationBuilder.Sql(@"DELETE FROM ""LuckLeaderboardEntries"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LuckLeaderboardEntries_Generation_Board_Score",
                table: "LuckLeaderboardEntries");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "LuckLeaderboardEntries");

            migrationBuilder.AddColumn<int>(
                name: "Tier",
                table: "LuckLeaderboardEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_LuckLeaderboardEntries_Generation_Board_Tier",
                table: "LuckLeaderboardEntries",
                columns: new[] { "Generation", "Board", "Tier" });
        }
    }
}
