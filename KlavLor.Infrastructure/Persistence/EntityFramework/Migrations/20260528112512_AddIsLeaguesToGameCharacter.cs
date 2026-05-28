using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddIsLeaguesToGameCharacter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLeagues",
                table: "GameCharacters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_GameCharacters_IsLeagues_IsVisible_IsAdminHidden",
                table: "GameCharacters",
                columns: new[] { "IsLeagues", "IsVisible", "IsAdminHidden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameCharacters_IsLeagues_IsVisible_IsAdminHidden",
                table: "GameCharacters");

            migrationBuilder.DropColumn(
                name: "IsLeagues",
                table: "GameCharacters");
        }
    }
}
