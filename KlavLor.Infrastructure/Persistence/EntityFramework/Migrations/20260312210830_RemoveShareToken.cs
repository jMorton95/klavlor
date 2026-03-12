using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class RemoveShareToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Templates_ShareToken",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "ShareToken",
                table: "Templates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShareToken",
                table: "Templates",
                type: "character varying(22)",
                maxLength: 22,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_ShareToken",
                table: "Templates",
                column: "ShareToken",
                unique: true);
        }
    }
}
