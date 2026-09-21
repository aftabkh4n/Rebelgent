using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rebelgent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActorType = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HumanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdentityProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ExternalIdentityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ApprovedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AuditEventId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedByHumanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    SuspendedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RetiredAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PromptTemplate = table.Column<string>(type: "TEXT", nullable: false),
                    Capabilities = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderConfigurationReference = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedByProposalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EvaluationSummary = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentEvolutionProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalType = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetAgentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProposedAgentName = table.Column<string>(type: "TEXT", nullable: true),
                    ProposedRole = table.Column<int>(type: "INTEGER", nullable: true),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: false),
                    SuggestedChange = table.Column<string>(type: "TEXT", nullable: false),
                    SuggestedPrompt = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestedCapabilities = table.Column<string>(type: "TEXT", nullable: true),
                    RiskLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    EvaluationSummary = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ApprovedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedTaskId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentEvolutionProposals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_SequenceNumber",
                table: "AuditEvents",
                column: "SequenceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TimestampUtc",
                table: "AuditEvents",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EventType",
                table: "AuditEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRecords_ResourceId",
                table: "ApprovalRecords",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRecords_HumanId",
                table: "ApprovalRecords",
                column: "HumanId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentDefinitions_Status",
                table: "AgentDefinitions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AgentDefinitions_Name",
                table: "AgentDefinitions",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_AgentVersions_AgentDefinitionId",
                table: "AgentVersions",
                column: "AgentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentEvolutionProposals_Status",
                table: "AgentEvolutionProposals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AgentEvolutionProposals_ProposalType",
                table: "AgentEvolutionProposals",
                column: "ProposalType");

            // SQLite immutability triggers for AuditEvents
            migrationBuilder.Sql(@"
CREATE TRIGGER prevent_audit_events_update
BEFORE UPDATE ON AuditEvents
BEGIN
    SELECT RAISE(ABORT, 'AuditEvents are immutable: updates are not permitted');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER prevent_audit_events_delete
BEFORE DELETE ON AuditEvents
BEGIN
    SELECT RAISE(ABORT, 'AuditEvents are immutable: deletes are not permitted');
END;");

            // SQLite immutability triggers for ApprovalRecords
            migrationBuilder.Sql(@"
CREATE TRIGGER prevent_approval_records_update
BEFORE UPDATE ON ApprovalRecords
BEGIN
    SELECT RAISE(ABORT, 'ApprovalRecords are immutable: updates are not permitted');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER prevent_approval_records_delete
BEFORE DELETE ON ApprovalRecords
BEGIN
    SELECT RAISE(ABORT, 'ApprovalRecords are immutable: deletes are not permitted');
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS prevent_audit_events_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS prevent_audit_events_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS prevent_approval_records_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS prevent_approval_records_delete;");

            migrationBuilder.DropTable(name: "AuditEvents");
            migrationBuilder.DropTable(name: "ApprovalRecords");
            migrationBuilder.DropTable(name: "AgentDefinitions");
            migrationBuilder.DropTable(name: "AgentVersions");
            migrationBuilder.DropTable(name: "AgentEvolutionProposals");
        }
    }
}
