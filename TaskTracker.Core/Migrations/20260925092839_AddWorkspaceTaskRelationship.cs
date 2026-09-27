using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceTaskRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "TaskRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskRequests_WorkspaceId",
                table: "TaskRequests",
                column: "WorkspaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskRequests_Workspaces_WorkspaceId",
                table: "TaskRequests",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskRequests_Workspaces_WorkspaceId",
                table: "TaskRequests");

            migrationBuilder.DropIndex(
                name: "IX_TaskRequests_WorkspaceId",
                table: "TaskRequests");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "TaskRequests");
        }
    }
}
