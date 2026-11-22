using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskPlanningSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskPlanningSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskRequestId = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    InitialUtcOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    InitialPriority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InitialDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OriginalDueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InitialPlannedCalendarDays = table.Column<int>(type: "integer", nullable: true),
                    InitialPlannedDurationSeconds = table.Column<long>(type: "bigint", nullable: true),
                    InitialAssignmentState = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsWorkspaceTask = table.Column<bool>(type: "boolean", nullable: false),
                    InitialVisibility = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InitialTitleLength = table.Column<int>(type: "integer", nullable: false),
                    InitialDescriptionLength = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskPlanningSnapshots", x => x.Id);
                    table.CheckConstraint("CK_TaskPlanningSnapshots_DeadlineConsistency", "(\"InitialDueDate\" IS NULL AND \"OriginalDueAtUtc\" IS NULL AND \"InitialPlannedCalendarDays\" IS NULL AND \"InitialPlannedDurationSeconds\" IS NULL) OR (\"InitialDueDate\" IS NOT NULL AND \"OriginalDueAtUtc\" IS NOT NULL AND \"InitialPlannedCalendarDays\" IS NOT NULL AND \"InitialPlannedDurationSeconds\" IS NOT NULL)");
                    table.CheckConstraint("CK_TaskPlanningSnapshots_TextLengths", "\"InitialTitleLength\" > 0 AND \"InitialTitleLength\" <= 150 AND \"InitialDescriptionLength\" > 0 AND \"InitialDescriptionLength\" <= 10000");
                    table.CheckConstraint("CK_TaskPlanningSnapshots_UtcOffsetMinutes", "\"InitialUtcOffsetMinutes\" BETWEEN -720 AND 840");
                    table.ForeignKey(
                        name: "FK_TaskPlanningSnapshots_TaskRequests_TaskRequestId",
                        column: x => x.TaskRequestId,
                        principalTable: "TaskRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskPlanningSnapshots_CreatedAtUtc_Id",
                table: "TaskPlanningSnapshots",
                columns: new[] { "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskPlanningSnapshots_TaskRequestId",
                table: "TaskPlanningSnapshots",
                column: "TaskRequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskPlanningSnapshots");
        }
    }
}
