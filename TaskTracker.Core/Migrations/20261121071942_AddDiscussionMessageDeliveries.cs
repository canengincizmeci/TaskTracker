using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscussionMessageDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskMessages_Content",
                table: "TaskMessages");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "TaskMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryFileCount",
                table: "TaskMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryFingerprint",
                table: "TaskMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskMessages_TaskRequestId_SenderUserId_ClientRequestId",
                table: "TaskMessages",
                columns: new[] { "TaskRequestId", "SenderUserId", "ClientRequestId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskMessages_Content",
                table: "TaskMessages",
                sql: "(length(trim(\"Content\")) > 0 OR (\"Content\" = '' AND \"DeliveryFileCount\" > 0)) AND length(\"Content\") <= 4000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskMessages_Delivery",
                table: "TaskMessages",
                sql: "(\"ClientRequestId\" IS NULL AND \"DeliveryFingerprint\" IS NULL AND \"DeliveryFileCount\" = 0) OR (\"ClientRequestId\" IS NOT NULL AND \"DeliveryFingerprint\" IS NOT NULL AND length(\"DeliveryFingerprint\") = 64 AND \"DeliveryFileCount\" BETWEEN 0 AND 10)");

            // The message is inserted before its attachment rows. Validate the complete
            // delivery at commit, including callers outside the EF write guard.
            migrationBuilder.Sql("""
                CREATE FUNCTION check_task_message_delivery_files() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."DeliveryFileCount" <> (
                        SELECT count(*) FROM "TaskAttachments"
                        WHERE "TaskMessageId" = NEW."Id"
                          AND "TaskRequestId" = NEW."TaskRequestId"
                          AND "UploaderUserId" = NEW."SenderUserId"
                          AND "Kind" = 1 AND NOT "IsDeleted"
                    ) THEN
                        RAISE EXCEPTION 'A discussion delivery requires all its attachments'
                            USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE CONSTRAINT TRIGGER "CT_TaskMessages_DeliveryFiles"
                    AFTER INSERT ON "TaskMessages"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                    WHEN (NEW."DeliveryFileCount" > 0)
                    EXECUTE FUNCTION check_task_message_delivery_files();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "CT_TaskMessages_DeliveryFiles" ON "TaskMessages";
                DROP FUNCTION check_task_message_delivery_files();
                """);

            migrationBuilder.DropIndex(
                name: "IX_TaskMessages_TaskRequestId_SenderUserId_ClientRequestId",
                table: "TaskMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskMessages_Content",
                table: "TaskMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskMessages_Delivery",
                table: "TaskMessages");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "TaskMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryFileCount",
                table: "TaskMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryFingerprint",
                table: "TaskMessages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskMessages_Content",
                table: "TaskMessages",
                sql: "length(trim(\"Content\")) > 0 AND length(\"Content\") <= 4000");
        }
    }
}
