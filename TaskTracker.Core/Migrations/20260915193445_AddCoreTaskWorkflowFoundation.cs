using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreTaskWorkflowFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssigneeUserId",
                table: "TaskRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "TaskRequests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "TaskSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskRequestId = table.Column<int>(type: "integer", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSubmissions", x => x.Id);
                    table.CheckConstraint("CK_TaskSubmissions_Content", "length(trim(\"Content\")) > 0");
                    table.CheckConstraint("CK_TaskSubmissions_RevisionNumber", "\"RevisionNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_TaskSubmissions_TaskRequests_TaskRequestId",
                        column: x => x.TaskRequestId,
                        principalTable: "TaskRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskSubmissions_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskSubmissionReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskSubmissionId = table.Column<int>(type: "integer", nullable: false),
                    ReviewerUserId = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Feedback = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSubmissionReviews", x => x.Id);
                    table.CheckConstraint("CK_TaskSubmissionReviews_Decision", "\"Decision\" IN ('Approved', 'ChangesRequested')");
                    table.CheckConstraint("CK_TaskSubmissionReviews_Feedback", "\"Decision\" <> 'ChangesRequested' OR (\"Feedback\" IS NOT NULL AND length(trim(\"Feedback\")) > 0)");
                    table.ForeignKey(
                        name: "FK_TaskSubmissionReviews_TaskSubmissions_TaskSubmissionId",
                        column: x => x.TaskSubmissionId,
                        principalTable: "TaskSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskSubmissionReviews_Users_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskRequestId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    ActivityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    TargetUserId = table.Column<int>(type: "integer", nullable: true),
                    InvitationId = table.Column<int>(type: "integer", nullable: true),
                    SubmissionId = table.Column<int>(type: "integer", nullable: true),
                    ReviewId = table.Column<int>(type: "integer", nullable: true),
                    FromStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskActivities_TaskRequests_TaskRequestId",
                        column: x => x.TaskRequestId,
                        principalTable: "TaskRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskActivities_TaskShareInvitations_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "TaskShareInvitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskActivities_TaskSubmissionReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "TaskSubmissionReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskActivities_TaskSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "TaskSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskActivities_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskActivities_Users_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskRequests_AssigneeUserId",
                table: "TaskRequests",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskActivities_ActorUserId",
                table: "TaskActivities",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskActivities_InvitationId",
                table: "TaskActivities",
                column: "InvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskActivities_ReviewId",
                table: "TaskActivities",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskActivities_SubmissionId",
                table: "TaskActivities",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskActivities_TargetUserId",
                table: "TaskActivities",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskActivities_TaskRequestId_CreatedAt_Id",
                table: "TaskActivities",
                columns: new[] { "TaskRequestId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskSubmissionReviews_ReviewerUserId",
                table: "TaskSubmissionReviews",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSubmissionReviews_TaskSubmissionId",
                table: "TaskSubmissionReviews",
                column: "TaskSubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskSubmissions_SubmittedByUserId",
                table: "TaskSubmissions",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSubmissions_TaskRequestId_RevisionNumber",
                table: "TaskSubmissions",
                columns: new[] { "TaskRequestId", "RevisionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskRequests_Users_AssigneeUserId",
                table: "TaskRequests",
                column: "AssigneeUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskRequests_Users_AssigneeUserId",
                table: "TaskRequests");

            migrationBuilder.DropTable(
                name: "TaskActivities");

            migrationBuilder.DropTable(
                name: "TaskSubmissionReviews");

            migrationBuilder.DropTable(
                name: "TaskSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_TaskRequests_AssigneeUserId",
                table: "TaskRequests");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId",
                table: "TaskRequests");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "TaskRequests");
        }
    }
}
