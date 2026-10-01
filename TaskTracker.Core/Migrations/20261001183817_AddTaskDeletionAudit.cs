using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskDeletionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "TaskRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletedByUserId",
                table: "TaskRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskRequests_DeletedByUserId",
                table: "TaskRequests",
                column: "DeletedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskRequests_DeletionAudit",
                table: "TaskRequests",
                sql: "(\"Activity\" = TRUE AND \"DeletedAt\" IS NULL AND \"DeletedByUserId\" IS NULL) OR (\"Activity\" = FALSE AND ((\"DeletedAt\" IS NULL AND \"DeletedByUserId\" IS NULL) OR (\"DeletedAt\" IS NOT NULL AND \"DeletedByUserId\" IS NOT NULL)))");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskRequests_Users_DeletedByUserId",
                table: "TaskRequests",
                column: "DeletedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskRequests_Users_DeletedByUserId",
                table: "TaskRequests");

            migrationBuilder.DropIndex(
                name: "IX_TaskRequests_DeletedByUserId",
                table: "TaskRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskRequests_DeletionAudit",
                table: "TaskRequests");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "TaskRequests");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "TaskRequests");
        }
    }
}
