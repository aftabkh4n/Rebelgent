using Microsoft.EntityFrameworkCore;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence;

/// <summary>EF Core database context for Rebelgent.</summary>
public class RebelgentDbContext : DbContext
{
    internal DbSet<AgentTaskRecord> AgentTasks => Set<AgentTaskRecord>();
    internal DbSet<ApprovalRequestRecord> ApprovalRequests => Set<ApprovalRequestRecord>();
    internal DbSet<AgentExecutionDbRecord> AgentExecutions => Set<AgentExecutionDbRecord>();
    internal DbSet<ReleaseDbRecord> Releases => Set<ReleaseDbRecord>();
    internal DbSet<PackageDbRecord> Packages => Set<PackageDbRecord>();
    internal DbSet<ExecutionFailureDbRecord> ExecutionFailures => Set<ExecutionFailureDbRecord>();
    internal DbSet<ImprovementProposalDbRecord> ImprovementProposals => Set<ImprovementProposalDbRecord>();
    internal DbSet<EvaluationResultDbRecord> EvaluationResults => Set<EvaluationResultDbRecord>();
    internal DbSet<AuditEventDbRecord> AuditEvents => Set<AuditEventDbRecord>();
    internal DbSet<ApprovalRecordDbRecord> ApprovalRecords => Set<ApprovalRecordDbRecord>();
    internal DbSet<AgentDefinitionDbRecord> AgentDefinitions => Set<AgentDefinitionDbRecord>();
    internal DbSet<AgentVersionDbRecord> AgentVersions => Set<AgentVersionDbRecord>();
    internal DbSet<AgentEvolutionProposalDbRecord> AgentEvolutionProposals => Set<AgentEvolutionProposalDbRecord>();
    internal DbSet<SecurityAuditDeadLetterDbRecord> SecurityAuditDeadLetters => Set<SecurityAuditDeadLetterDbRecord>();
    internal DbSet<SecurityAuditDeadLetterRecoveryDbRecord> SecurityAuditDeadLetterRecoveries => Set<SecurityAuditDeadLetterRecoveryDbRecord>();

    public RebelgentDbContext(DbContextOptions<RebelgentDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentTaskRecord>(entity =>
        {
            entity.ToTable("AgentTasks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProjectId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.AssignedRole).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.Risk).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.StartedAt).HasColumnType("INTEGER");
            entity.Property(e => e.CompletedAt).HasColumnType("INTEGER");
            entity.Property(e => e.BranchName).HasMaxLength(500);
            entity.Property(e => e.PullRequestUrl).HasMaxLength(500);
            entity.Property(e => e.PullRequestCreatedAt).HasColumnType("INTEGER");
            entity.Property(e => e.MergedAt).HasColumnType("INTEGER");
            entity.Property(e => e.MergeCommitSha).HasMaxLength(40);
            entity.Property(e => e.MergeMethod).HasMaxLength(50);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<ReleaseDbRecord>(entity =>
        {
            entity.ToTable("Releases");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TaskId).IsRequired();
            entity.Property(e => e.Version).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Notes).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.TagName).IsRequired().HasMaxLength(30);
            entity.Property(e => e.GitHubReleaseUrl).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.PublishedAt).HasColumnType("INTEGER");
            entity.Property(e => e.MergeCommitSha).IsRequired().HasMaxLength(40);
            entity.HasIndex(e => e.TaskId).IsUnique();
        });

        modelBuilder.Entity<PackageDbRecord>(entity =>
        {
            entity.ToTable("Packages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TaskId).IsRequired();
            entity.Property(e => e.PackageId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.PackageVersion).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PackagePath).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.PreparedAt).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.PublishedAt).HasColumnType("INTEGER");
            entity.HasIndex(e => e.TaskId).IsUnique();
        });

        modelBuilder.Entity<ApprovalRequestRecord>(entity =>
        {
            entity.ToTable("ApprovalRequests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.RequestedAt).IsRequired();
            entity.HasIndex(e => e.TaskId);
        });

        modelBuilder.Entity<ExecutionFailureDbRecord>(entity =>
        {
            entity.ToTable("ExecutionFailures");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TaskId).IsRequired();
            entity.Property(e => e.Category).IsRequired();
            entity.Property(e => e.Source).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Message).IsRequired();
            entity.Property(e => e.DetectedAt).IsRequired().HasColumnType("INTEGER");
            entity.HasIndex(e => e.TaskId);
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.ExecutionId);
        });

        modelBuilder.Entity<ImprovementProposalDbRecord>(entity =>
        {
            entity.ToTable("ImprovementProposals");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TargetProjectId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.Evidence).IsRequired();
            entity.Property(e => e.TargetArea).IsRequired().HasMaxLength(200);
            entity.Property(e => e.SuggestedChange).IsRequired();
            entity.Property(e => e.RiskLevel).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.EvidenceFingerprint).IsRequired().HasMaxLength(64);
            entity.Property(e => e.DecidedAt).HasColumnType("INTEGER");
            entity.HasIndex(e => e.EvidenceFingerprint);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.TargetProjectId);
        });

        modelBuilder.Entity<EvaluationResultDbRecord>(entity =>
        {
            entity.ToTable("EvaluationResults");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProposalId).IsRequired();
            entity.Property(e => e.DatasetName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CasesJson).IsRequired();
            entity.Property(e => e.EvaluatedAt).IsRequired().HasColumnType("INTEGER");
            entity.HasIndex(e => e.ProposalId);
        });

        modelBuilder.Entity<AgentExecutionDbRecord>(entity =>
        {
            entity.ToTable("AgentExecutions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProjectId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.WorkspacePath).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.BranchName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Role).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(100).HasDefaultValue("ClaudeCode");
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.StartedAt).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.CompletedAt).HasColumnType("INTEGER");
            entity.Property(e => e.Findings).HasMaxLength(4000);
            entity.Property(e => e.CommitSha).HasMaxLength(40);
            entity.HasIndex(e => e.TaskId);
            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => new { e.TaskId, e.Role });
        });

        modelBuilder.Entity<AuditEventDbRecord>(entity =>
        {
            entity.ToTable("AuditEvents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SequenceNumber).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.TimestampUtc).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ActorType).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.ActorId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ResourceType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ResourceId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(200);
            entity.Property(e => e.PayloadJson).IsRequired();
            entity.Property(e => e.PreviousHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Hash).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.SequenceNumber).IsUnique();
            entity.HasIndex(e => e.TimestampUtc);
            entity.HasIndex(e => e.EventType);
        });

        modelBuilder.Entity<ApprovalRecordDbRecord>(entity =>
        {
            entity.ToTable("ApprovalRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActionType).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ResourceId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.HumanId).IsRequired();
            entity.Property(e => e.IdentityProvider).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ExternalIdentityId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ApprovedAt).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.RequestId).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.ResourceId);
            entity.HasIndex(e => e.HumanId);
        });

        modelBuilder.Entity<AgentDefinitionDbRecord>(entity =>
        {
            entity.ToTable("AgentDefinitions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Role).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.Purpose).IsRequired();
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.CreatedByHumanId).IsRequired();
            entity.Property(e => e.ActivatedAt).HasColumnType("INTEGER");
            entity.Property(e => e.SuspendedAt).HasColumnType("INTEGER");
            entity.Property(e => e.RetiredAt).HasColumnType("INTEGER");
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Name);
        });

        modelBuilder.Entity<AgentVersionDbRecord>(entity =>
        {
            entity.ToTable("AgentVersions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AgentDefinitionId).IsRequired();
            entity.Property(e => e.Version).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PromptTemplate).IsRequired();
            entity.Property(e => e.Capabilities).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.Status).IsRequired().HasColumnType("INTEGER");
            entity.HasIndex(e => e.AgentDefinitionId);
        });

        modelBuilder.Entity<AgentEvolutionProposalDbRecord>(entity =>
        {
            entity.ToTable("AgentEvolutionProposals");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProposalType).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.TargetProjectId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Purpose).IsRequired();
            entity.Property(e => e.Evidence).IsRequired();
            entity.Property(e => e.SuggestedChange).IsRequired();
            entity.Property(e => e.RiskLevel).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.Status).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.ApprovedAt).HasColumnType("INTEGER");
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ProposalType);
        });

        modelBuilder.Entity<SecurityAuditDeadLetterDbRecord>(entity =>
        {
            entity.ToTable("SecurityAuditDeadLetters");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TimestampUtc).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ActorType).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.ActorId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ResourceType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ResourceId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(200);
            entity.Property(e => e.PayloadJson).IsRequired();
            entity.Property(e => e.PrimaryAuditError).IsRequired().HasMaxLength(4000);
            entity.HasIndex(e => e.TimestampUtc);
            entity.HasIndex(e => e.EventType);
        });

        modelBuilder.Entity<SecurityAuditDeadLetterRecoveryDbRecord>(entity =>
        {
            entity.ToTable("SecurityAuditDeadLetterRecoveries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeadLetterId).IsRequired();
            entity.Property(e => e.RecoveredAuditEventId).IsRequired();
            entity.Property(e => e.RecoveredAt).IsRequired().HasColumnType("INTEGER");
            entity.Property(e => e.RecoveredByHumanId).IsRequired();
            entity.HasIndex(e => e.DeadLetterId);
        });
    }
}
