using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionLogExclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectionLogExclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() AT TIME ZONE 'UTC'"),
                    SavedById = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionLogExclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectionLogExclusions_Users_SavedById",
                        column: x => x.SavedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionLogExclusions_ItemId",
                table: "CollectionLogExclusions",
                column: "ItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionLogExclusions_SavedById",
                table: "CollectionLogExclusions",
                column: "SavedById");

            // Effective collection-log set = synced wiki items minus the admin blacklist.
            // Read-side clog queries point at this view so exclusions apply everywhere.
            migrationBuilder.Sql("""
                CREATE VIEW "EffectiveCollectionLogItems" AS
                SELECT cli.* FROM "CollectionLogItems" cli
                WHERE NOT EXISTS (
                    SELECT 1 FROM "CollectionLogExclusions" ce WHERE ce."ItemId" = cli."ItemId"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS \"EffectiveCollectionLogItems\";");

            migrationBuilder.DropTable(
                name: "CollectionLogExclusions");
        }
    }
}
