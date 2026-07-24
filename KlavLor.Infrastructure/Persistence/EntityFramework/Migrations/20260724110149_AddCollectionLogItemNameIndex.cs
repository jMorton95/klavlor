using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionLogItemNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expression index backing the case-insensitive name fallback in GetSourceCollection
            // (clog obtained/missing matching). Without it, lower("Name") = lower(...) is a full
            // clog scan per drop, which pushed the luck-leaderboard refresh into multi-minute runs
            // and command timeouts. EF's fluent API can't express a functional index, so raw SQL.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_CollectionLogItems_LowerName\" ON \"CollectionLogItems\" (lower(\"Name\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CollectionLogItems_LowerName\";");
        }
    }
}
