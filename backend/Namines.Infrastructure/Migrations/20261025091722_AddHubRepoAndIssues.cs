using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubRepoAndIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "CloudProjects",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "CloudProjects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ForkCount",
                table: "CloudProjects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ForkedFromProjectId",
                table: "CloudProjects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedGithubRepo",
                table: "CloudProjects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "CloudProjects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StarCount",
                table: "CloudProjects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "CloudProjects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "CloudProjects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RepoIssues",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    IssueNumber = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    RelatedTableName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    NslRuleCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Labels = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepoIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepoIssues_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepoIssues_CloudProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "CloudProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepoStars",
                columns: table => new
                {
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepoStars", x => new { x.ProjectId, x.UserId });
                    table.ForeignKey(
                        name: "FK_RepoStars_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepoStars_CloudProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "CloudProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepoIssueComments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IssueId = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    AuthorUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepoIssueComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepoIssueComments_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepoIssueComments_RepoIssues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "RepoIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudProjects_Slug",
                table: "CloudProjects",
                column: "Slug");

            migrationBuilder.CreateIndex(
                name: "IX_CloudProjects_Visibility",
                table: "CloudProjects",
                column: "Visibility");

            migrationBuilder.CreateIndex(
                name: "IX_RepoIssueComments_AuthorUserId",
                table: "RepoIssueComments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RepoIssueComments_IssueId",
                table: "RepoIssueComments",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_RepoIssues_CreatedByUserId",
                table: "RepoIssues",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RepoIssues_ProjectId_IsClosed",
                table: "RepoIssues",
                columns: new[] { "ProjectId", "IsClosed" });

            migrationBuilder.CreateIndex(
                name: "IX_RepoIssues_ProjectId_IssueNumber",
                table: "RepoIssues",
                columns: new[] { "ProjectId", "IssueNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepoStars_UserId",
                table: "RepoStars",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepoIssueComments");

            migrationBuilder.DropTable(
                name: "RepoStars");

            migrationBuilder.DropTable(
                name: "RepoIssues");

            migrationBuilder.DropIndex(
                name: "IX_CloudProjects_Slug",
                table: "CloudProjects");

            migrationBuilder.DropIndex(
                name: "IX_CloudProjects_Visibility",
                table: "CloudProjects");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "CloudProjects");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "CloudProjects");

            migrationBuilder.DropColumn(
                name: "ForkCount",
                table: "CloudProjects");

            migrationBuilder.DropColumn(
                name: "ForkedFromProjectId",
                table: "CloudProjects");

            migrationBuilder.DropColumn(
                name: "LinkedGithubRepo",
                table: "CloudProjects");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "CloudProjects");

            migrationBuilder.DropColumn(
                name: "StarCount",
                table: "CloudProjects");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "CloudProjects");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "CloudProjects");
        }
    }
}
