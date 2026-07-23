using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaderboardSourceExclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaderboardSourceExclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() AT TIME ZONE 'UTC'"),
                    SavedById = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardSourceExclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaderboardSourceExclusions_Users_SavedById",
                        column: x => x.SavedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardSourceExclusions_SavedById",
                table: "LeaderboardSourceExclusions",
                column: "SavedById");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardSourceExclusions_SourceName",
                table: "LeaderboardSourceExclusions",
                column: "SourceName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaderboardSourceExclusions");
        }
    }
}
