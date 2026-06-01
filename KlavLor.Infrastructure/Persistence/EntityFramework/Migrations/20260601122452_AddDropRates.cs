using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddDropRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DropRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ItemName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    Rarity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RarityNumerator = table.Column<int>(type: "integer", nullable: true),
                    RarityDenominator = table.Column<int>(type: "integer", nullable: true),
                    Rolls = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Quantity = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Notes = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DropRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DropRates_SourceName",
                table: "DropRates",
                column: "SourceName");

            migrationBuilder.CreateIndex(
                name: "IX_DropRates_SourceName_ItemName",
                table: "DropRates",
                columns: new[] { "SourceName", "ItemName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DropRates");
        }
    }
}
