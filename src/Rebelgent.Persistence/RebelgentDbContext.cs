using Microsoft.EntityFrameworkCore;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence;

/// <summary>EF Core database context for Rebelgent.</summary>
public class RebelgentDbContext : DbContext
{
    internal DbSet<AgentTaskRecord> AgentTasks => Set<AgentTaskRecord>();
    internal DbSet<ApprovalRequestRecord> ApprovalRequests => Set<ApprovalRequestRecord>();
    internal DbSet<AgentExecutionDbRecord> AgentExecutions => Set<AgentExecutionDbRecord>();

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
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<ApprovalRequestRecord>(entity =>
        {
            entity.ToTable("ApprovalRequests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.RequestedAt).IsRequired();
            entity.HasIndex(e => e.TaskId);
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
    }
}
