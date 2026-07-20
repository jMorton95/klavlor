using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddLuckLeaderboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LuckLeaderboardEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    GameCharacterId = table.Column<int>(type: "integer", nullable: false),
                    CharacterName = table.Column<string>(type: "text", nullable: false),
                    SourceName = table.Column<string>(type: "text", nullable: false),
                    ItemName = table.Column<string>(type: "text", nullable: false),
                    Board = table.Column<string>(type: "text", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    Multiple = table.Column<double>(type: "double precision", nullable: false),
                    Obtained = table.Column<bool>(type: "boolean", nullable: false),
                    ObservedKc = table.Column<int>(type: "integer", nullable: false),
                    ExpectedKc = table.Column<double>(type: "double precision", nullable: false),
                    RarityDenominator = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() AT TIME ZONE 'UTC'"),
                    SavedById = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LuckLeaderboardEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LuckLeaderboardEntries_Users_SavedById",
                        column: x => x.SavedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LuckLeaderboardMeta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CurrentGeneration = table.Column<long>(type: "bigint", nullable: false),
                    RefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() AT TIME ZONE 'UTC'"),
                    SavedById = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LuckLeaderboardMeta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LuckLeaderboardMeta_Users_SavedById",
                        column: x => x.SavedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LuckLeaderboardEntries_Generation_Board_Tier",
                table: "LuckLeaderboardEntries",
                columns: new[] { "Generation", "Board", "Tier" });

            migrationBuilder.CreateIndex(
                name: "IX_LuckLeaderboardEntries_SavedById",
                table: "LuckLeaderboardEntries",
                column: "SavedById");

            migrationBuilder.CreateIndex(
                name: "IX_LuckLeaderboardMeta_SavedById",
                table: "LuckLeaderboardMeta",
                column: "SavedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LuckLeaderboardEntries");

            migrationBuilder.DropTable(
                name: "LuckLeaderboardMeta");
        }
    }
}
