using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSubTaskSubmissionDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SubTaskSubmissions_Content",
                table: "SubTaskSubmissions");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "SubTaskSubmissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryFileCount",
                table: "SubTaskSubmissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryFingerprint",
                table: "SubTaskSubmissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskSubmissions_SubTaskId_SubmittedByUserId_ClientReques~",
                table: "SubTaskSubmissions",
                columns: new[] { "SubTaskId", "SubmittedByUserId", "ClientRequestId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubTaskSubmissions_Content",
                table: "SubTaskSubmissions",
                sql: "(length(trim(\"Content\", U&'\\0020\\0009\\000A\\000B\\000C\\000D\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')) > 0 OR (\"Content\" = '' AND \"DeliveryFileCount\" > 0)) AND length(\"Content\") <= 10000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubTaskSubmissions_Delivery",
                table: "SubTaskSubmissions",
                sql: "(\"ClientRequestId\" IS NULL AND \"DeliveryFingerprint\" IS NULL AND \"DeliveryFileCount\" = 0) OR (\"ClientRequestId\" IS NOT NULL AND \"DeliveryFingerprint\" IS NOT NULL AND length(\"DeliveryFingerprint\") = 64 AND \"DeliveryFileCount\" BETWEEN 0 AND 10)");

            migrationBuilder.Sql("""
                CREATE FUNCTION check_subtask_submission_delivery_files() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."DeliveryFileCount" <> (
                        SELECT count(*) FROM "TaskAttachments"
                        WHERE "SubTaskSubmissionId" = NEW."Id"
                          AND "TaskRequestId" = NEW."TaskRequestId"
                          AND "UploaderUserId" = NEW."SubmittedByUserId"
                          AND "Kind" = 3 AND NOT "IsDeleted"
                    ) THEN
                        RAISE EXCEPTION 'A subtask submission delivery requires all its attachments'
                            USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE CONSTRAINT TRIGGER "CT_SubTaskSubmissions_DeliveryFiles"
                    AFTER INSERT ON "SubTaskSubmissions"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                    WHEN (NEW."DeliveryFileCount" > 0)
                    EXECUTE FUNCTION check_subtask_submission_delivery_files();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "CT_SubTaskSubmissions_DeliveryFiles" ON "SubTaskSubmissions";
                DROP FUNCTION check_subtask_submission_delivery_files();
                """);

            migrationBuilder.DropIndex(
                name: "IX_SubTaskSubmissions_SubTaskId_SubmittedByUserId_ClientReques~",
                table: "SubTaskSubmissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubTaskSubmissions_Content",
                table: "SubTaskSubmissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubTaskSubmissions_Delivery",
                table: "SubTaskSubmissions");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "SubTaskSubmissions");

            migrationBuilder.DropColumn(
                name: "DeliveryFileCount",
                table: "SubTaskSubmissions");

            migrationBuilder.DropColumn(
                name: "DeliveryFingerprint",
                table: "SubTaskSubmissions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubTaskSubmissions_Content",
                table: "SubTaskSubmissions",
                sql: "length(trim(\"Content\", U&'\\0020\\0009\\000A\\000B\\000C\\000D\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')) > 0");
        }
    }
}
