using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rebelgent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionRoleAndFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "AgentExecutions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3); // AgentRole.BackendDeveloper = 3

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "AgentExecutions",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "ClaudeCode");

            migrationBuilder.AddColumn<string>(
                name: "Findings",
                table: "AgentExecutions",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BuildSucceeded",
                table: "AgentExecutions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TestsSucceeded",
                table: "AgentExecutions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutions_TaskId_Role",
                table: "AgentExecutions",
                columns: new[] { "TaskId", "Role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentExecutions_TaskId_Role",
                table: "AgentExecutions");

            migrationBuilder.DropColumn(name: "Role", table: "AgentExecutions");
            migrationBuilder.DropColumn(name: "Provider", table: "AgentExecutions");
            migrationBuilder.DropColumn(name: "Findings", table: "AgentExecutions");
            migrationBuilder.DropColumn(name: "BuildSucceeded", table: "AgentExecutions");
            migrationBuilder.DropColumn(name: "TestsSucceeded", table: "AgentExecutions");
        }
    }
}
