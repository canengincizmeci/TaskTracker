using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class SeedOperationClaimsReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
        INSERT INTO "OperationClaims" ("Id", "Name")
        VALUES
            (1, 'Admin'),
            (2, 'User')
        ON CONFLICT ("Id")
        DO UPDATE SET "Name" = EXCLUDED."Name";

        SELECT setval(
            pg_get_serial_sequence('"OperationClaims"', 'Id')::regclass,
            (SELECT MAX("Id") FROM "OperationClaims"),
            true
        );
        """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally left empty.
            // These are application reference-data records and may have
            // existed before this migration on existing databases.
        }
    }
}
