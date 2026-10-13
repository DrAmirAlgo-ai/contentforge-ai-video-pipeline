using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentEmpireSystem.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveApprovalLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalLogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VideoProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    TelegramCallbackData = table.Column<string>(type: "TEXT", nullable: true),
                    TelegramMessageId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalLogs_VideoProjects_VideoProjectId",
                        column: x => x.VideoProjectId,
                        principalTable: "VideoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalLogs_VideoProjectId",
                table: "ApprovalLogs",
                column: "VideoProjectId");
        }
    }
}
