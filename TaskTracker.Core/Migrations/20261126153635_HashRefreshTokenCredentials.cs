using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class HashRefreshTokenCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plaintext may already exist in backups or logs. Invalidate every old session.
            migrationBuilder.Sql("""TRUNCATE TABLE "RefreshTokens";""");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "RefreshTokens");

            migrationBuilder.AddColumn<string>(
                name: "TokenDigest",
                table: "RefreshTokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenDigest",
                table: "RefreshTokens",
                column: "TokenDigest",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Existing digests cannot be converted back to usable legacy credentials.
            migrationBuilder.Sql("""TRUNCATE TABLE "RefreshTokens";""");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_TokenDigest",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "TokenDigest",
                table: "RefreshTokens");

            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "RefreshTokens",
                type: "text",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token");
        }
    }
}
