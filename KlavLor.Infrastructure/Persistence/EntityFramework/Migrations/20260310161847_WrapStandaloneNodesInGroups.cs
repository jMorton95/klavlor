using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class WrapStandaloneNodesInGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // For each standalone node (GroupId IS NULL), create a group at the node's position
            // and assign the node to that group
            migrationBuilder.Sql("""
                INSERT INTO "TemplateNodeGroups" ("TemplateId", "PositionX", "PositionY", "SavedAt", "SavedById")
                SELECT n."TemplateId", n."PositionX", n."PositionY", NOW(), n."SavedById"
                FROM "TemplateNodes" n
                WHERE n."GroupId" IS NULL;

                UPDATE "TemplateNodes" n
                SET "GroupId" = g."Id"
                FROM "TemplateNodeGroups" g
                WHERE n."GroupId" IS NULL
                  AND g."TemplateId" = n."TemplateId"
                  AND g."PositionX" = n."PositionX"
                  AND g."PositionY" = n."PositionY";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No reversal needed
        }
    }
}
