using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentEmpireSystem.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CategoryType = table.Column<int>(type: "INTEGER", nullable: false),
                    NameAr = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsSecret = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentIdea = table.Column<string>(type: "TEXT", nullable: true),
                    ConfirmedIdea = table.Column<string>(type: "TEXT", nullable: true),
                    ScriptJson = table.Column<string>(type: "TEXT", nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", nullable: true),
                    AudioFilePath = table.Column<string>(type: "TEXT", nullable: true),
                    WordTimestampsJson = table.Column<string>(type: "TEXT", nullable: true),
                    VideoFilePath = table.Column<string>(type: "TEXT", nullable: true),
                    AssSubtitlePath = table.Column<string>(type: "TEXT", nullable: true),
                    AiProviderUsed = table.Column<string>(type: "TEXT", nullable: true),
                    TtsProviderUsed = table.Column<string>(type: "TEXT", nullable: true),
                    ImageProviderUsed = table.Column<string>(type: "TEXT", nullable: true),
                    VideoProviderUsed = table.Column<string>(type: "TEXT", nullable: true),
                    ProductionWorkflowPath = table.Column<string>(type: "TEXT", nullable: false),
                    ContentLanguage = table.Column<string>(type: "TEXT", nullable: false),
                    TargetWordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetDurationSec = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageSegmentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoSegmentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImagesPer15Sec = table.Column<int>(type: "INTEGER", nullable: false),
                    VideosPer15Sec = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoProjects_ContentCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ContentCategories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VideoProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    TelegramCallbackData = table.Column<string>(type: "TEXT", nullable: true),
                    TelegramMessageId = table.Column<long>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "GeneratedImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VideoProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    ScenePrompt = table.Column<string>(type: "TEXT", nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    LocalFilePath = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    VisualPartIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTimeMs = table.Column<double>(type: "REAL", nullable: false),
                    EndTimeMs = table.Column<double>(type: "REAL", nullable: false),
                    AnimationMode = table.Column<string>(type: "TEXT", nullable: false),
                    MotionSpeed = table.Column<double>(type: "REAL", nullable: false),
                    MaxZoom = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneratedImages_VideoProjects_VideoProjectId",
                        column: x => x.VideoProjectId,
                        principalTable: "VideoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalLogs_VideoProjectId",
                table: "ApprovalLogs",
                column: "VideoProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentCategories_CategoryType",
                table: "ContentCategories",
                column: "CategoryType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedImages_VideoProjectId",
                table: "GeneratedImages",
                column: "VideoProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoProjects_CategoryId",
                table: "VideoProjects",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoProjects_CreatedAt",
                table: "VideoProjects",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_VideoProjects_Status",
                table: "VideoProjects",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalLogs");

            migrationBuilder.DropTable(
                name: "GeneratedImages");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "VideoProjects");

            migrationBuilder.DropTable(
                name: "ContentCategories");
        }
    }
}
