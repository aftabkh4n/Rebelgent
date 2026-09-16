using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rebelgent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddObservability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvaluationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DatasetName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TotalCases = table.Column<int>(type: "INTEGER", nullable: false),
                    PassedCases = table.Column<int>(type: "INTEGER", nullable: false),
                    PassRate = table.Column<double>(type: "REAL", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CasesJson = table.Column<string>(type: "TEXT", nullable: false),
                    EvaluatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionFailures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    DetectedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionFailures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImprovementProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: false),
                    TargetArea = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SuggestedChange = table.Column<string>(type: "TEXT", nullable: false),
                    RiskLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    EvaluationSummary = table.Column<string>(type: "TEXT", nullable: true),
                    EvidenceFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImprovementProposals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationResults_ProposalId",
                table: "EvaluationResults",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionFailures_Category",
                table: "ExecutionFailures",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionFailures_ExecutionId",
                table: "ExecutionFailures",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionFailures_TaskId",
                table: "ExecutionFailures",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementProposals_EvidenceFingerprint",
                table: "ImprovementProposals",
                column: "EvidenceFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementProposals_Status",
                table: "ImprovementProposals",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationResults");

            migrationBuilder.DropTable(
                name: "ExecutionFailures");

            migrationBuilder.DropTable(
                name: "ImprovementProposals");
        }
    }
}
