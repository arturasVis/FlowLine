using FlowLine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Infrastructure.Data;

public class FlowLineDbContext(DbContextOptions<FlowLineDbContext> options) : DbContext(options)
{
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<Stage> Stages => Set<Stage>();
    public DbSet<Step> Steps => Set<Step>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<StepCompletion> StepCompletions => Set<StepCompletion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Template side: Workflow -> Stage -> Step -> MediaAsset. Deleting a definition
        // cascades through its own structure.
        modelBuilder.Entity<Workflow>()
            .HasMany(w => w.Stages)
            .WithOne(s => s.Workflow)
            .HasForeignKey(s => s.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Stage>()
            .HasMany(s => s.Steps)
            .WithOne(st => st.Stage)
            .HasForeignKey(st => st.StageId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Step>()
            .HasMany(st => st.MediaAssets)
            .WithOne(m => m.Step)
            .HasForeignKey(m => m.StepId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Stage>()
            .HasMany(s => s.Stations)
            .WithOne(st => st.Stage)
            .HasForeignKey(st => st.StageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Instance side: restrict deletes that would silently destroy WorkItem/timing
        // history out from under a still-referenced Workflow, Stage, or Step.
        modelBuilder.Entity<WorkItem>()
            .HasOne(wi => wi.Workflow)
            .WithMany(w => w.WorkItems)
            .HasForeignKey(wi => wi.WorkflowId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkItem>()
            .HasOne(wi => wi.CurrentStage)
            .WithMany(s => s.CurrentWorkItems)
            .HasForeignKey(wi => wi.CurrentStageId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkItem>()
            .HasOne(wi => wi.ClaimedByStation)
            .WithMany(s => s.ClaimedWorkItems)
            .HasForeignKey(wi => wi.ClaimedByStationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StepCompletion>()
            .HasOne(sc => sc.WorkItem)
            .WithMany(wi => wi.StepCompletions)
            .HasForeignKey(sc => sc.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StepCompletion>()
            .HasOne(sc => sc.Step)
            .WithMany(st => st.Completions)
            .HasForeignKey(sc => sc.StepId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StepCompletion>()
            .HasOne(sc => sc.Station)
            .WithMany(s => s.Completions)
            .HasForeignKey(sc => sc.StationId)
            .OnDelete(DeleteBehavior.SetNull);

        // Supports the per-stage queue scan, the atomic claim's WHERE Status = Queued
        // AND ClaimedByStationId IS NULL guard, and ordering by oldest-queued (PRD §6.5).
        modelBuilder.Entity<WorkItem>()
            .HasIndex(wi => new { wi.CurrentStageId, wi.Status, wi.ClaimedByStationId, wi.QueuedAtUtc });

        // SQLite has no native rowversion type, so WorkItem.RowVersion is an
        // application-managed concurrency token — see SaveChanges below and NFR-4.
        modelBuilder.Entity<WorkItem>()
            .Property(wi => wi.RowVersion)
            .IsConcurrencyToken();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void BumpConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries<IConcurrencyAware>())
        {
            if (entry.State is EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid();
            }
        }
    }
}
