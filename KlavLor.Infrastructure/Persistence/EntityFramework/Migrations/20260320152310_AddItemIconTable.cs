using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddItemIconTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemIcons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    CachedImageId = table.Column<int>(type: "integer", nullable: true),
                    FailCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemIcons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemIcons_CachedImages_CachedImageId",
                        column: x => x.CachedImageId,
                        principalTable: "CachedImages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemIcons_CachedImageId",
                table: "ItemIcons",
                column: "CachedImageId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemIcons_ItemName",
                table: "ItemIcons",
                column: "ItemName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemIcons");
        }
    }
}
