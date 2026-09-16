using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rebelgent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImprovementProposalTargetProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetProjectId",
                table: "ImprovementProposals",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementProposals_TargetProjectId",
                table: "ImprovementProposals",
                column: "TargetProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImprovementProposals_TargetProjectId",
                table: "ImprovementProposals");

            migrationBuilder.DropColumn(
                name: "TargetProjectId",
                table: "ImprovementProposals");
        }
    }
}
