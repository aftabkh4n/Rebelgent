using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rebelgent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityAuditDeadLetter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecurityAuditDeadLetterRecoveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeadLetterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecoveredAuditEventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecoveredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RecoveredByHumanId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAuditDeadLetterRecoveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAuditDeadLetters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActorType = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryAuditError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAuditDeadLetters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditDeadLetterRecoveries_DeadLetterId",
                table: "SecurityAuditDeadLetterRecoveries",
                column: "DeadLetterId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditDeadLetters_EventType",
                table: "SecurityAuditDeadLetters",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditDeadLetters_TimestampUtc",
                table: "SecurityAuditDeadLetters",
                column: "TimestampUtc");

            // SQLite immutability triggers — dead-letter records and their recovery linkage rows
            // are part of permanent institutional history. No UPDATE or DELETE ever, at any level.
            migrationBuilder.Sql(@"
CREATE TRIGGER prevent_security_audit_dead_letters_update
BEFORE UPDATE ON SecurityAuditDeadLetters
BEGIN
    SELECT RAISE(ABORT, 'SecurityAuditDeadLetters are immutable: updates are not permitted');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER prevent_security_audit_dead_letters_delete
BEFORE DELETE ON SecurityAuditDeadLetters
BEGIN
    SELECT RAISE(ABORT, 'SecurityAuditDeadLetters are immutable: deletes are not permitted');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER prevent_security_audit_dead_letter_recoveries_update
BEFORE UPDATE ON SecurityAuditDeadLetterRecoveries
BEGIN
    SELECT RAISE(ABORT, 'SecurityAuditDeadLetterRecoveries are immutable: updates are not permitted');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER prevent_security_audit_dead_letter_recoveries_delete
BEFORE DELETE ON SecurityAuditDeadLetterRecoveries
BEGIN
    SELECT RAISE(ABORT, 'SecurityAuditDeadLetterRecoveries are immutable: deletes are not permitted');
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS prevent_security_audit_dead_letters_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS prevent_security_audit_dead_letters_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS prevent_security_audit_dead_letter_recoveries_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS prevent_security_audit_dead_letter_recoveries_delete;");

            migrationBuilder.DropTable(
                name: "SecurityAuditDeadLetterRecoveries");

            migrationBuilder.DropTable(
                name: "SecurityAuditDeadLetters");
        }
    }
}
