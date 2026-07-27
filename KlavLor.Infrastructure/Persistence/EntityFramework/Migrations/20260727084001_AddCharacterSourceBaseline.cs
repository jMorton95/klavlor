using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterSourceBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterSourceBaselines",
                columns: table => new
                {
                    GameCharacterId = table.Column<int>(type: "integer", nullable: false),
                    SourceName = table.Column<string>(type: "text", nullable: false),
                    BaselineKc = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterSourceBaselines", x => new { x.GameCharacterId, x.SourceName });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterSourceBaselines");
        }
    }
}
