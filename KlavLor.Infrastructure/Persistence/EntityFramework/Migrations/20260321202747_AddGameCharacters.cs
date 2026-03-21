using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddGameCharacters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameCharacterId",
                table: "LootRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GameCharacters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RuneLiteId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    IsAdminHidden = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() AT TIME ZONE 'UTC'"),
                    SavedById = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameCharacters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameCharacters_Users_SavedById",
                        column: x => x.SavedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GameCharacters_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LootRecords_GameCharacterId_OccurredAt",
                table: "LootRecords",
                columns: new[] { "GameCharacterId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LootRecords_GameCharacterId_SourceName",
                table: "LootRecords",
                columns: new[] { "GameCharacterId", "SourceName" });

            migrationBuilder.CreateIndex(
                name: "IX_GameCharacters_SavedById",
                table: "GameCharacters",
                column: "SavedById");

            migrationBuilder.CreateIndex(
                name: "IX_GameCharacters_UserId_RuneLiteId",
                table: "GameCharacters",
                columns: new[] { "UserId", "RuneLiteId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LootRecords_GameCharacters_GameCharacterId",
                table: "LootRecords",
                column: "GameCharacterId",
                principalTable: "GameCharacters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LootRecords_GameCharacters_GameCharacterId",
                table: "LootRecords");

            migrationBuilder.DropTable(
                name: "GameCharacters");

            migrationBuilder.DropIndex(
                name: "IX_LootRecords_GameCharacterId_OccurredAt",
                table: "LootRecords");

            migrationBuilder.DropIndex(
                name: "IX_LootRecords_GameCharacterId_SourceName",
                table: "LootRecords");

            migrationBuilder.DropColumn(
                name: "GameCharacterId",
                table: "LootRecords");
        }
    }
}
