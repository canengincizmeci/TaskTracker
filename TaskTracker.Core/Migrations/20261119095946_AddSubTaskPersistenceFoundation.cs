using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSubTaskPersistenceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubTaskSubmissionId",
                table: "TaskAttachments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskRequestId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    AssigneeUserId = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Activity = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubTasks", x => x.Id);
                    table.UniqueConstraint("AK_SubTasks_Id_TaskRequestId", x => new { x.Id, x.TaskRequestId });
                    table.CheckConstraint("CK_SubTasks_DeletionAudit", "(\"Activity\" = TRUE AND \"DeletedAt\" IS NULL AND \"DeletedByUserId\" IS NULL) OR (\"Activity\" = FALSE AND \"DeletedAt\" IS NOT NULL AND \"DeletedByUserId\" IS NOT NULL AND \"DeletedAt\" >= \"CreatedAt\")");
                    table.CheckConstraint("CK_SubTasks_Description", "length(trim(\"Description\", U&'\\0020\\0009\\000A\\000B\\000C\\000D\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')) > 0");
                    table.CheckConstraint("CK_SubTasks_Priority", "\"Priority\" IN ('Low', 'Medium', 'High', 'Critical')");
                    table.CheckConstraint("CK_SubTasks_Status", "\"Status\" IN ('Pending', 'InProgress', 'Completed', 'Cancelled', 'InReview')");
                    table.CheckConstraint("CK_SubTasks_Title", "length(trim(\"Title\", U&'\\0020\\0009\\000A\\000B\\000C\\000D\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')) > 0");
                    table.CheckConstraint("CK_SubTasks_Version", "\"Version\" >= 0");
                    table.ForeignKey(
                        name: "FK_SubTasks_TaskRequests_TaskRequestId",
                        column: x => x.TaskRequestId,
                        principalTable: "TaskRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubTasks_Users_AssigneeUserId",
                        column: x => x.AssigneeUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubTasks_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubTasks_Users_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubTaskSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubTaskId = table.Column<int>(type: "integer", nullable: false),
                    TaskRequestId = table.Column<int>(type: "integer", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubTaskSubmissions", x => x.Id);
                    table.UniqueConstraint("AK_SubTaskSubmissions_Id_SubTaskId", x => new { x.Id, x.SubTaskId });
                    table.UniqueConstraint("AK_SubTaskSubmissions_Id_TaskRequestId", x => new { x.Id, x.TaskRequestId });
                    table.CheckConstraint("CK_SubTaskSubmissions_Content", "length(trim(\"Content\", U&'\\0020\\0009\\000A\\000B\\000C\\000D\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')) > 0");
                    table.CheckConstraint("CK_SubTaskSubmissions_RevisionNumber", "\"RevisionNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_SubTaskSubmissions_SubTasks_SubTaskId_TaskRequestId",
                        columns: x => new { x.SubTaskId, x.TaskRequestId },
                        principalTable: "SubTasks",
                        principalColumns: new[] { "Id", "TaskRequestId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubTaskSubmissions_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubTaskSubmissionReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubTaskSubmissionId = table.Column<int>(type: "integer", nullable: false),
                    ReviewerUserId = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Feedback = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubTaskSubmissionReviews", x => x.Id);
                    table.UniqueConstraint("AK_SubTaskSubmissionReviews_Id_SubTaskSubmissionId", x => new { x.Id, x.SubTaskSubmissionId });
                    table.CheckConstraint("CK_SubTaskSubmissionReviews_Decision", "\"Decision\" IN ('Approved', 'ChangesRequested')");
                    table.CheckConstraint("CK_SubTaskSubmissionReviews_Feedback", "\"Decision\" <> 'ChangesRequested' OR (\"Feedback\" IS NOT NULL AND length(trim(\"Feedback\", U&'\\0020\\0009\\000A\\000B\\000C\\000D\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')) > 0)");
                    table.ForeignKey(
                        name: "FK_SubTaskSubmissionReviews_SubTaskSubmissions_SubTaskSubmissi~",
                        column: x => x.SubTaskSubmissionId,
                        principalTable: "SubTaskSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubTaskSubmissionReviews_Users_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubTaskActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskRequestId = table.Column<int>(type: "integer", nullable: false),
                    SubTaskId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    ActivityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    TargetUserId = table.Column<int>(type: "integer", nullable: true),
                    SubmissionId = table.Column<int>(type: "integer", nullable: true),
                    ReviewId = table.Column<int>(type: "integer", nullable: true),
                    FromStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubTaskActivities", x => x.Id);
                    table.CheckConstraint("CK_SubTaskActivities_DeletionStatus", "\"ActivityType\" <> 'TaskDeleted' OR (\"FromStatus\" IS NULL AND \"ToStatus\" IS NULL)");
                    table.CheckConstraint("CK_SubTaskActivities_FromStatus", "\"FromStatus\" IN ('Pending', 'InProgress', 'Completed', 'Cancelled', 'InReview')");
                    table.CheckConstraint("CK_SubTaskActivities_Review", "(\"ActivityType\" IN ('ChangesRequested', 'SubmissionApproved')) = (\"ReviewId\" IS NOT NULL)");
                    table.CheckConstraint("CK_SubTaskActivities_Submission", "(\"ActivityType\" IN ('SubmissionCreated', 'ChangesRequested', 'SubmissionApproved')) = (\"SubmissionId\" IS NOT NULL)");
                    table.CheckConstraint("CK_SubTaskActivities_Target", "(\"ActivityType\" IN ('UserAssigned', 'UserUnassigned')) = (\"TargetUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_SubTaskActivities_ToStatus", "\"ToStatus\" IN ('Pending', 'InProgress', 'Completed', 'Cancelled', 'InReview')");
                    table.CheckConstraint("CK_SubTaskActivities_Type", "\"ActivityType\" IN ('TaskCreated', 'TaskDetailsUpdated', 'TaskDeleted', 'UserAssigned', 'UserUnassigned', 'WorkStarted', 'SubmissionCreated', 'ChangesRequested', 'SubmissionApproved', 'TaskCompleted', 'TaskCancelled', 'TaskReopened', 'ResponsibilityReset')");
                    table.ForeignKey(
                        name: "FK_SubTaskActivities_SubTaskSubmissionReviews_ReviewId_Submiss~",
                        columns: x => new { x.ReviewId, x.SubmissionId },
                        principalTable: "SubTaskSubmissionReviews",
                        principalColumns: new[] { "Id", "SubTaskSubmissionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubTaskActivities_SubTaskSubmissions_SubmissionId_SubTaskId",
                        columns: x => new { x.SubmissionId, x.SubTaskId },
                        principalTable: "SubTaskSubmissions",
                        principalColumns: new[] { "Id", "SubTaskId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubTaskActivities_SubTasks_SubTaskId_TaskRequestId",
                        columns: x => new { x.SubTaskId, x.TaskRequestId },
                        principalTable: "SubTasks",
                        principalColumns: new[] { "Id", "TaskRequestId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubTaskActivities_TaskRequests_TaskRequestId",
                        column: x => x.TaskRequestId,
                        principalTable: "TaskRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubTaskActivities_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubTaskActivities_Users_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskAttachments_SubTaskSubmissionId_TaskRequestId",
                table: "TaskAttachments",
                columns: new[] { "SubTaskSubmissionId", "TaskRequestId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskAttachments_Context",
                table: "TaskAttachments",
                sql: "(\"Kind\" = 0 AND \"TaskMessageId\" IS NULL AND \"TaskSubmissionId\" IS NULL AND \"SubTaskSubmissionId\" IS NULL) OR (\"Kind\" = 1 AND \"TaskMessageId\" IS NOT NULL AND \"TaskSubmissionId\" IS NULL AND \"SubTaskSubmissionId\" IS NULL) OR (\"Kind\" = 2 AND \"TaskMessageId\" IS NULL AND \"TaskSubmissionId\" IS NOT NULL AND \"SubTaskSubmissionId\" IS NULL) OR (\"Kind\" = 3 AND \"TaskMessageId\" IS NULL AND \"TaskSubmissionId\" IS NULL AND \"SubTaskSubmissionId\" IS NOT NULL AND NOT \"IsDeleted\" AND \"DeletedAt\" IS NULL AND \"DeletedByUserId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskActivities_ActorUserId",
                table: "SubTaskActivities",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskActivities_ReviewId_SubmissionId",
                table: "SubTaskActivities",
                columns: new[] { "ReviewId", "SubmissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskActivities_SubmissionId_SubTaskId",
                table: "SubTaskActivities",
                columns: new[] { "SubmissionId", "SubTaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskActivities_SubTaskId_CreatedAt_Id",
                table: "SubTaskActivities",
                columns: new[] { "SubTaskId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskActivities_SubTaskId_TaskRequestId",
                table: "SubTaskActivities",
                columns: new[] { "SubTaskId", "TaskRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskActivities_TargetUserId",
                table: "SubTaskActivities",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskActivities_TaskRequestId_CreatedAt_Id",
                table: "SubTaskActivities",
                columns: new[] { "TaskRequestId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SubTasks_AssigneeUserId",
                table: "SubTasks",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubTasks_CreatedByUserId",
                table: "SubTasks",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubTasks_DeletedByUserId",
                table: "SubTasks",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubTasks_TaskRequestId_CreatedAt_Id",
                table: "SubTasks",
                columns: new[] { "TaskRequestId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskSubmissionReviews_ReviewerUserId",
                table: "SubTaskSubmissionReviews",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskSubmissionReviews_SubTaskSubmissionId",
                table: "SubTaskSubmissionReviews",
                column: "SubTaskSubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskSubmissions_SubmittedByUserId",
                table: "SubTaskSubmissions",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskSubmissions_SubTaskId_RevisionNumber",
                table: "SubTaskSubmissions",
                columns: new[] { "SubTaskId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskSubmissions_SubTaskId_TaskRequestId",
                table: "SubTaskSubmissions",
                columns: new[] { "SubTaskId", "TaskRequestId" });

            migrationBuilder.AddForeignKey(
                name: "FK_TaskAttachments_SubTaskSubmissions_SubTaskSubmissionId_Task~",
                table: "TaskAttachments",
                columns: new[] { "SubTaskSubmissionId", "TaskRequestId" },
                principalTable: "SubTaskSubmissions",
                principalColumns: new[] { "Id", "TaskRequestId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskAttachments_SubTaskSubmissions_SubTaskSubmissionId_Task~",
                table: "TaskAttachments");

            migrationBuilder.DropTable(
                name: "SubTaskActivities");

            migrationBuilder.DropTable(
                name: "SubTaskSubmissionReviews");

            migrationBuilder.DropTable(
                name: "SubTaskSubmissions");

            migrationBuilder.DropTable(
                name: "SubTasks");

            migrationBuilder.DropIndex(
                name: "IX_TaskAttachments_SubTaskSubmissionId_TaskRequestId",
                table: "TaskAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskAttachments_Context",
                table: "TaskAttachments");

            migrationBuilder.DropColumn(
                name: "SubTaskSubmissionId",
                table: "TaskAttachments");
        }
    }
}
