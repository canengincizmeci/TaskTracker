using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SessionVersion",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "RefreshTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Legacy refresh tokens cannot be associated with a session-bearing JWT.
            migrationBuilder.Sql("""UPDATE "RefreshTokens" SET "IsRevoked" = TRUE WHERE NOT "IsRevoked";""");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_SessionId_IsRevoked",
                table: "RefreshTokens",
                columns: new[] { "SessionId", "IsRevoked" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back the schema must not reactivate invalidated credentials.
            migrationBuilder.Sql("""UPDATE "RefreshTokens" SET "IsRevoked" = TRUE WHERE NOT "IsRevoked";""");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_SessionId_IsRevoked",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "RefreshTokens");
        }
    }
}
