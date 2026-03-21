using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceIcons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceIcons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CachedImageId = table.Column<int>(type: "integer", nullable: true),
                    FailCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceIcons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceIcons_CachedImages_CachedImageId",
                        column: x => x.CachedImageId,
                        principalTable: "CachedImages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceIcons_CachedImageId",
                table: "SourceIcons",
                column: "CachedImageId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceIcons_SourceName",
                table: "SourceIcons",
                column: "SourceName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceIcons");
        }
    }
}
