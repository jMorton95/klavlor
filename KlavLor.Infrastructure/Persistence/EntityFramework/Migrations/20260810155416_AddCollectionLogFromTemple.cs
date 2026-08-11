using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionLogFromTemple : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterCollectionLogEntries",
                columns: table => new
                {
                    GameCharacterId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    ObtainedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterCollectionLogEntries", x => new { x.GameCharacterId, x.ItemId });
                    table.ForeignKey(
                        name: "FK_CharacterCollectionLogEntries_GameCharacters_GameCharacterId",
                        column: x => x.GameCharacterId,
                        principalTable: "GameCharacters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterCollectionLogStates",
                columns: table => new
                {
                    GameCharacterId = table.Column<int>(type: "integer", nullable: false),
                    Rsn = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TempleDisplayName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GameMode = table.Column<int>(type: "integer", nullable: false),
                    TotalObtained = table.Column<int>(type: "integer", nullable: false),
                    TotalAvailable = table.Column<int>(type: "integer", nullable: false),
                    CategoriesFinished = table.Column<int>(type: "integer", nullable: false),
                    CategoriesAvailable = table.Column<int>(type: "integer", nullable: false),
                    HiscoresRank = table.Column<int>(type: "integer", nullable: true),
                    TempleLastChecked = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TempleLastChanged = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastOutcome = table.Column<string>(type: "text", nullable: false),
                    LastError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterCollectionLogStates", x => x.GameCharacterId);
                    table.ForeignKey(
                        name: "FK_CharacterCollectionLogStates_GameCharacters_GameCharacterId",
                        column: x => x.GameCharacterId,
                        principalTable: "GameCharacters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionLogCategories",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    GroupName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ItemCount = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionLogCategories", x => x.Slug);
                });

            migrationBuilder.CreateTable(
                name: "CollectionLogCategoryItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CategorySlug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionLogCategoryItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterCollectionLogEntries_ItemId",
                table: "CharacterCollectionLogEntries",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterCollectionLogStates_TotalObtained",
                table: "CharacterCollectionLogStates",
                column: "TotalObtained");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionLogCategories_GroupName_SortOrder",
                table: "CollectionLogCategories",
                columns: new[] { "GroupName", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionLogCategoryItems_CategorySlug_ItemId",
                table: "CollectionLogCategoryItems",
                columns: new[] { "CategorySlug", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionLogCategoryItems_ItemId",
                table: "CollectionLogCategoryItems",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterCollectionLogEntries");

            migrationBuilder.DropTable(
                name: "CharacterCollectionLogStates");

            migrationBuilder.DropTable(
                name: "CollectionLogCategories");

            migrationBuilder.DropTable(
                name: "CollectionLogCategoryItems");
        }
    }
}
