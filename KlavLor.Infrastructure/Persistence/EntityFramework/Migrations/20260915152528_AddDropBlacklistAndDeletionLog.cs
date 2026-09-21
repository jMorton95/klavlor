using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddDropBlacklistAndDeletionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlacklistedLootDrops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LootRecordId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() AT TIME ZONE 'UTC'"),
                    SavedById = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlacklistedLootDrops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlacklistedLootDrops_LootRecords_LootRecordId",
                        column: x => x.LootRecordId,
                        principalTable: "LootRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BlacklistedLootDrops_Users_SavedById",
                        column: x => x.SavedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DeletedLootRecordLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LootRecordId = table.Column<int>(type: "integer", nullable: false),
                    GameCharacterId = table.Column<int>(type: "integer", nullable: true),
                    CharacterName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    KillCount = table.Column<int>(type: "integer", nullable: true),
                    TotalValue = table.Column<long>(type: "bigint", nullable: false),
                    DropsSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() AT TIME ZONE 'UTC'"),
                    SavedById = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletedLootRecordLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeletedLootRecordLogs_Users_SavedById",
                        column: x => x.SavedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistedLootDrops_LootRecordId",
                table: "BlacklistedLootDrops",
                column: "LootRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistedLootDrops_SavedById",
                table: "BlacklistedLootDrops",
                column: "SavedById");

            migrationBuilder.CreateIndex(
                name: "IX_DeletedLootRecordLogs_SavedAt",
                table: "DeletedLootRecordLogs",
                column: "SavedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_DeletedLootRecordLogs_SavedById",
                table: "DeletedLootRecordLogs",
                column: "SavedById");

            // One blacklist decision per (record, item), matched case-insensitively — the same rule
            // the projection rebuild SQL and DropBlacklistCache apply. Hand-written because EF
            // cannot model an expression index (see IX_LootRecords_SourceNameLower for the other
            // one). Without it a double-click could stack two rows for the same drop and leave the
            // second behind when the first is lifted, silently keeping the item hidden.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "IX_BlacklistedLootDrops_Record_Item_NameLower"
                ON "BlacklistedLootDrops" ("LootRecordId", "ItemId", lower("ItemName"))
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_BlacklistedLootDrops_Record_Item_NameLower" """);

            migrationBuilder.DropTable(
                name: "BlacklistedLootDrops");

            migrationBuilder.DropTable(
                name: "DeletedLootRecordLogs");
        }
    }
}
