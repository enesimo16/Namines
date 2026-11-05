using Microsoft.EntityFrameworkCore.Migrations;
using Namines.BuildingBlocks.Persistence;

#nullable disable

namespace Namines.Hub.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Hub tablolarını Auth zincirinden devralır ve etiket bağına proje bütünlüğü ekler (BL-C1).
    /// RepoStars, RepoIssues, RepoIssueComments yerinde taşınır. repo_issue_labels'a ProjectId eklenip
    /// issue'dan doldurulur; iki FK de ProjectId'yi içerir: başka projenin etiketi artık DB'de de reddedilir.
    /// Doğrulama adımı: bugün proje dışı bağ varsa migration NET hatayla durur, veri sessizce düzeltilmez.
    /// </summary>
    public partial class Hub_01_AdoptTables : Migration
    {
        private static readonly string[] Tables = { "RepoStars", "RepoIssues", "RepoIssueComments" };

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ModuleTableHandoff.BeginMove(migrationBuilder, "hub");
            ModuleTableHandoff.MoveTables(migrationBuilder, "public", "hub", Tables);

            migrationBuilder.Sql("""
                ALTER TABLE hub.repo_issue_labels ADD COLUMN "ProjectId" text;
                UPDATE hub.repo_issue_labels l SET "ProjectId" = i."ProjectId"
                  FROM hub."RepoIssues" i WHERE i."Id" = l."RepoIssueId";
                DO $$
                DECLARE bad bigint;
                BEGIN
                  SELECT count(*) INTO bad
                    FROM hub.repo_issue_labels l
                    JOIN hub.issue_labels x ON x."Id" = l."IssueLabelId"
                   WHERE x."ProjectId" <> l."ProjectId";
                  IF bad > 0 THEN
                    RAISE EXCEPTION 'Hub_01: % etiket bagi baska projenin etiketine isaret ediyor; once veriyi inceleyin.', bad;
                  END IF;
                END $$;
                ALTER TABLE hub.repo_issue_labels ALTER COLUMN "ProjectId" SET NOT NULL;

                ALTER TABLE hub."RepoIssues" ADD CONSTRAINT "AK_RepoIssues_Id_ProjectId" UNIQUE ("Id", "ProjectId");
                ALTER TABLE hub.issue_labels ADD CONSTRAINT "AK_issue_labels_Id_ProjectId" UNIQUE ("Id", "ProjectId");

                ALTER TABLE hub.repo_issue_labels
                  ADD CONSTRAINT "FK_repo_issue_labels_RepoIssues_Project"
                  FOREIGN KEY ("RepoIssueId", "ProjectId") REFERENCES hub."RepoIssues" ("Id", "ProjectId") ON DELETE CASCADE;
                ALTER TABLE hub.repo_issue_labels
                  ADD CONSTRAINT "FK_repo_issue_labels_issue_labels_Project"
                  FOREIGN KEY ("IssueLabelId", "ProjectId") REFERENCES hub.issue_labels ("Id", "ProjectId") ON DELETE CASCADE;
                ALTER TABLE hub.repo_issue_labels DROP CONSTRAINT "FK_repo_issue_labels_RepoIssues_RepoIssueId";
                ALTER TABLE hub.repo_issue_labels DROP CONSTRAINT "FK_repo_issue_labels_issue_labels_IssueLabelId";

                CREATE INDEX "IX_repo_issue_labels_IssueLabelId_ProjectId" ON hub.repo_issue_labels ("IssueLabelId", "ProjectId");
                CREATE INDEX "IX_repo_issue_labels_RepoIssueId_ProjectId" ON hub.repo_issue_labels ("RepoIssueId", "ProjectId");
                """);

            ModuleTableHandoff.GrantAppRole(migrationBuilder, "hub");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"SET LOCAL lock_timeout = '{ModuleTableHandoff.LockTimeout}';");
            migrationBuilder.Sql("""
                DROP INDEX hub."IX_repo_issue_labels_RepoIssueId_ProjectId";
                DROP INDEX hub."IX_repo_issue_labels_IssueLabelId_ProjectId";
                ALTER TABLE hub.repo_issue_labels
                  ADD CONSTRAINT "FK_repo_issue_labels_RepoIssues_RepoIssueId"
                  FOREIGN KEY ("RepoIssueId") REFERENCES hub."RepoIssues" ("Id") ON DELETE CASCADE;
                ALTER TABLE hub.repo_issue_labels
                  ADD CONSTRAINT "FK_repo_issue_labels_issue_labels_IssueLabelId"
                  FOREIGN KEY ("IssueLabelId") REFERENCES hub.issue_labels ("Id") ON DELETE CASCADE;
                ALTER TABLE hub.repo_issue_labels DROP CONSTRAINT "FK_repo_issue_labels_RepoIssues_Project";
                ALTER TABLE hub.repo_issue_labels DROP CONSTRAINT "FK_repo_issue_labels_issue_labels_Project";
                ALTER TABLE hub.issue_labels DROP CONSTRAINT "AK_issue_labels_Id_ProjectId";
                ALTER TABLE hub."RepoIssues" DROP CONSTRAINT "AK_RepoIssues_Id_ProjectId";
                ALTER TABLE hub.repo_issue_labels DROP COLUMN "ProjectId";
                """);
            ModuleTableHandoff.MoveTables(migrationBuilder, "hub", "public", Tables);
        }
    }
}
