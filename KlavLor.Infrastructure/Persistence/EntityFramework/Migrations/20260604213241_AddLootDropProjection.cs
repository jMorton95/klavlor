using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddLootDropProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LootDrops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LootRecordId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<int>(type: "integer", nullable: false),
                    IsFirstTime = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LootDrops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LootDrops_LootRecords_LootRecordId",
                        column: x => x.LootRecordId,
                        principalTable: "LootRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LootDrops_IsFirstTime",
                table: "LootDrops",
                column: "IsFirstTime",
                filter: "\"IsFirstTime\"");

            migrationBuilder.CreateIndex(
                name: "IX_LootDrops_ItemId",
                table: "LootDrops",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_LootDrops_LootRecordId",
                table: "LootDrops",
                column: "LootRecordId");

            // gin_trgm index for item-name ILIKE search (EF's fluent API can't express
            // operator-class indexes). pg_trgm was already installed by the search-index
            // migration; guard anyway so this is safe on a fresh database.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_LootDrops_Name_trgm"
                ON "LootDrops" USING gin ("Name" gin_trgm_ops);
                """);

            // One-time backfill: project every existing LootRecord's DropsJson array into
            // rows. Runs exactly once (tracked in __EFMigrationsHistory). DropsJson is the
            // canonical source and is left untouched — these rows are fully rebuildable
            // from it. Empty arrays ('[]') yield no rows.
            migrationBuilder.Sql("""
                INSERT INTO "LootDrops" ("LootRecordId", "ItemId", "Name", "Quantity", "Price", "IsFirstTime")
                SELECT lr."Id",
                       COALESCE((d->>'ItemId')::int, 0),
                       COALESCE(d->>'Name', ''),
                       COALESCE((d->>'Quantity')::int, 0),
                       COALESCE((d->>'Price')::int, 0),
                       COALESCE((d->>'IsFirstTime')::boolean, false)
                FROM "LootRecords" lr,
                     jsonb_array_elements(lr."DropsJson") AS d;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LootDrops");
        }
    }
}
