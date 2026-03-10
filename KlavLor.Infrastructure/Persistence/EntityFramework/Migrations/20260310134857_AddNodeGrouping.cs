using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeGrouping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GroupId",
                table: "TemplateNodes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TemplateNodeGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    PositionX = table.Column<double>(type: "double precision", nullable: false),
                    PositionY = table.Column<double>(type: "double precision", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() AT TIME ZONE 'UTC'"),
                    SavedById = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateNodeGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateNodeGroups_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TemplateNodeGroups_Users_SavedById",
                        column: x => x.SavedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateNodes_GroupId",
                table: "TemplateNodes",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateNodeGroups_SavedById",
                table: "TemplateNodeGroups",
                column: "SavedById");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateNodeGroups_TemplateId",
                table: "TemplateNodeGroups",
                column: "TemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_TemplateNodes_TemplateNodeGroups_GroupId",
                table: "TemplateNodes",
                column: "GroupId",
                principalTable: "TemplateNodeGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TemplateNodes_TemplateNodeGroups_GroupId",
                table: "TemplateNodes");

            migrationBuilder.DropTable(
                name: "TemplateNodeGroups");

            migrationBuilder.DropIndex(
                name: "IX_TemplateNodes_GroupId",
                table: "TemplateNodes");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "TemplateNodes");
        }
    }
}
