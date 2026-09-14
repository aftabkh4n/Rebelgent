using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rebelgent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMergeInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MergedAt",
                table: "AgentTasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MergeCommitSha",
                table: "AgentTasks",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MergeMethod",
                table: "AgentTasks",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MergedAt", table: "AgentTasks");
            migrationBuilder.DropColumn(name: "MergeCommitSha", table: "AgentTasks");
            migrationBuilder.DropColumn(name: "MergeMethod", table: "AgentTasks");
        }
    }
}
