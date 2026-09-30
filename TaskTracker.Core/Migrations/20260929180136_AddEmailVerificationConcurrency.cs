using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerificationConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailVerifications_UserId",
                table: "EmailVerifications");

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "EmailVerifications",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                """
                UPDATE "EmailVerifications" AS existing
                SET "IsVerified" = TRUE,
                    "Version" = existing."Version" + 1
                WHERE "IsVerified" = FALSE
                  AND EXISTS (
                      SELECT 1
                      FROM "EmailVerifications" AS newer
                      WHERE newer."UserId" = existing."UserId"
                        AND newer."IsVerified" = FALSE
                        AND (
                            newer."CreatedAt" > existing."CreatedAt"
                            OR (newer."CreatedAt" = existing."CreatedAt" AND newer."Id" > existing."Id")
                        )
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "UX_EmailVerifications_UserId_Active",
                table: "EmailVerifications",
                column: "UserId",
                unique: true,
                filter: "\"IsVerified\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_EmailVerifications_UserId_Active",
                table: "EmailVerifications");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "EmailVerifications");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerifications_UserId",
                table: "EmailVerifications",
                column: "UserId");
        }
    }
}
