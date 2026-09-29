using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedUserName",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "Users"
                        GROUP BY lower(btrim("Email"))
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Identity normalization aborted: canonical email collision detected.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "Users"
                        GROUP BY lower(btrim("UserName"))
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Identity normalization aborted: normalized username collision detected.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "Users"
                        WHERE btrim("Email") = '' OR btrim("UserName") = ''
                    ) THEN
                        RAISE EXCEPTION 'Identity normalization aborted: blank email or username detected.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "Users"
                        WHERE btrim("Email") !~ '^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$'
                    ) THEN
                        RAISE EXCEPTION 'Identity normalization aborted: malformed email detected.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "Users"
                        WHERE char_length(lower(btrim("Email"))) > 200
                           OR char_length(btrim("UserName")) > 50
                           OR btrim("UserName") ~ '[[:cntrl:]]'
                    ) THEN
                        RAISE EXCEPTION 'Identity normalization aborted: invalid canonical identity length or username control character detected.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Users"
                SET "Email" = lower(btrim("Email")),
                    "UserName" = btrim("UserName"),
                    "NormalizedUserName" = lower(btrim("UserName"));
                """);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedUserName",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_Users_UserName",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedUserName",
                table: "Users",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Email canonicalization is intentionally irreversible: original casing and
            // surrounding whitespace cannot be reconstructed.
            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedUserName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedUserName",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);
        }
    }
}
