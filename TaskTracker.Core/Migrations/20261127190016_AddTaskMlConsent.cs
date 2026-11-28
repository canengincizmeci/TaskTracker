using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskMlConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MlConsentGranted",
                table: "TaskRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MlConsentGrantedAt",
                table: "TaskRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MlConsentPolicyVersion",
                table: "TaskRequests",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MlConsentRevokedAt",
                table: "TaskRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskMlConsentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskRequestId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    Granted = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskMlConsentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskMlConsentEvents_TaskRequests_TaskRequestId",
                        column: x => x.TaskRequestId,
                        principalTable: "TaskRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskMlConsentEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskRequests_MlConsent",
                table: "TaskRequests",
                sql: "(\"MlConsentGranted\" AND \"MlConsentGrantedAt\" IS NOT NULL AND \"MlConsentRevokedAt\" IS NULL AND \"MlConsentPolicyVersion\" IS NOT NULL) OR (NOT \"MlConsentGranted\" AND ((\"MlConsentGrantedAt\" IS NULL AND \"MlConsentRevokedAt\" IS NULL AND \"MlConsentPolicyVersion\" IS NULL) OR (\"MlConsentGrantedAt\" IS NOT NULL AND \"MlConsentRevokedAt\" IS NOT NULL AND \"MlConsentPolicyVersion\" IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_TaskMlConsentEvents_ActorUserId",
                table: "TaskMlConsentEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskMlConsentEvents_TaskRequestId_OccurredAtUtc_Id",
                table: "TaskMlConsentEvents",
                columns: new[] { "TaskRequestId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskRequests_MlDatasetConsented",
                table: "TaskRequests",
                column: "Id",
                filter: "\"Activity\" AND \"MlConsentGranted\"");

            migrationBuilder.Sql("""
                CREATE FUNCTION reject_task_ml_consent_event_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Task ML consent history is append-only' USING ERRCODE = '55000';
                END;
                $$;

                CREATE TRIGGER "TR_TaskMlConsentEvents_AppendOnly"
                BEFORE UPDATE OR DELETE OR TRUNCATE ON "TaskMlConsentEvents"
                FOR EACH STATEMENT EXECUTE FUNCTION reject_task_ml_consent_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskMlConsentEvents");
            migrationBuilder.Sql("DROP FUNCTION reject_task_ml_consent_event_change();");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskRequests_MlConsent",
                table: "TaskRequests");

            migrationBuilder.DropIndex(
                name: "IX_TaskRequests_MlDatasetConsented",
                table: "TaskRequests");

            migrationBuilder.DropColumn(
                name: "MlConsentGranted",
                table: "TaskRequests");

            migrationBuilder.DropColumn(
                name: "MlConsentGrantedAt",
                table: "TaskRequests");

            migrationBuilder.DropColumn(
                name: "MlConsentPolicyVersion",
                table: "TaskRequests");

            migrationBuilder.DropColumn(
                name: "MlConsentRevokedAt",
                table: "TaskRequests");
        }
    }
}
