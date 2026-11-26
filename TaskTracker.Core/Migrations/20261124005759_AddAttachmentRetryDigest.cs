using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentRetryDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentSha256",
                table: "TaskAttachments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskAttachments_TaskRequestId_UploaderUserId_Kind_ContentSh~",
                table: "TaskAttachments",
                columns: new[] { "TaskRequestId", "UploaderUserId", "Kind", "ContentSha256" },
                filter: "\"ContentSha256\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskAttachments_TaskRequestId_UploaderUserId_Kind_ContentSh~",
                table: "TaskAttachments");

            migrationBuilder.DropColumn(
                name: "ContentSha256",
                table: "TaskAttachments");
        }
    }
}
