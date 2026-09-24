using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rebelgent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvolutionImplementationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImplementationMergeCommitSha",
                table: "AgentEvolutionProposals",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImplementationPullRequestNumber",
                table: "AgentEvolutionProposals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ImplementedAt",
                table: "AgentEvolutionProposals",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImplementationMergeCommitSha",
                table: "AgentEvolutionProposals");

            migrationBuilder.DropColumn(
                name: "ImplementationPullRequestNumber",
                table: "AgentEvolutionProposals");

            migrationBuilder.DropColumn(
                name: "ImplementedAt",
                table: "AgentEvolutionProposals");
        }
    }
}
