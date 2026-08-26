using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rebelgent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    WorkspacePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    AgentOutput = table.Column<string>(type: "TEXT", nullable: true),
                    BuildOutput = table.Column<string>(type: "TEXT", nullable: true),
                    TestOutput = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentExecutions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutions_StartedAt",
                table: "AgentExecutions",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutions_TaskId",
                table: "AgentExecutions",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentExecutions");
        }
    }
}
