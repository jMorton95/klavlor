using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeSortOrderAndColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "TemplateNodes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "amber");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "TemplateNodes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill SortOrder from row number partitioned by GroupId ordered by Id
            migrationBuilder.Sql("""
                UPDATE "TemplateNodes" t
                SET "SortOrder" = sub.rn - 1
                FROM (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "GroupId" ORDER BY "Id") AS rn
                    FROM "TemplateNodes"
                ) sub
                WHERE t."Id" = sub."Id"
                """);

            // Backfill Color from NodeType
            migrationBuilder.Sql("""
                UPDATE "TemplateNodes"
                SET "Color" = CASE "NodeType"
                    WHEN 'Item' THEN 'amber'
                    WHEN 'Skill' THEN 'blue'
                    WHEN 'Prayer' THEN 'purple'
                    WHEN 'Quest' THEN 'green'
                    WHEN 'Construction' THEN 'orange'
                    WHEN 'Slayer' THEN 'red'
                    WHEN 'Spell' THEN 'indigo'
                    ELSE 'amber'
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "TemplateNodes");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "TemplateNodes");
        }
    }
}
