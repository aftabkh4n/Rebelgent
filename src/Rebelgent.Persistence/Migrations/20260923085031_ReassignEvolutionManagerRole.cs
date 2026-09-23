using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rebelgent.Persistence.Migrations
{
    /// <summary>
    /// Resolves the pre-M10 role-type collision between the "Agent Evolution Manager" and
    /// "Improvement Analyst" built-in agents. Both were previously registered under
    /// AgentRole.ImprovementAnalyst (int 14), which made role-based routing ambiguous.
    ///
    /// This migration reassigns any existing "Agent Evolution Manager" row to the newly
    /// introduced AgentRole.EvolutionManager (int 15). It only mutates the metadata
    /// column AgentDefinitions.Role — it does NOT touch the immutable audit ledger,
    /// dead-letter store, or lifecycle status.
    ///
    /// Down reverts the same rows to int 14 for symmetry.
    /// </summary>
    public partial class ReassignEvolutionManagerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE AgentDefinitions SET Role = 15 WHERE Name = 'Agent Evolution Manager' AND Role = 14;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE AgentDefinitions SET Role = 14 WHERE Name = 'Agent Evolution Manager' AND Role = 15;");
        }
    }
}
