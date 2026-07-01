using FlowLine.Domain.Entities;
using FlowLine.Domain.Entities.External;
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
    public DbSet<WorkflowAssignment> WorkflowAssignments => Set<WorkflowAssignment>();

    // Company-owned, pre-existing tables (SQL Server deployment only). Mapped read-only and
    // excluded from migrations — see ConfigureExternalTables below and the entity XML docs.
    public DbSet<HistoryRecord> History => Set<HistoryRecord>();
    public DbSet<StaffMember> Staff => Set<StaffMember>();

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

        // Workflow-to-staff assignment (FlowLine-owned). Deleting a workflow removes its
        // assignments; a workflow can't be assigned to the same staff number twice.
        modelBuilder.Entity<WorkflowAssignment>()
            .HasOne(a => a.Workflow)
            .WithMany(w => w.Assignments)
            .HasForeignKey(a => a.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WorkflowAssignment>()
            .HasIndex(a => new { a.WorkflowId, a.StaffNumber })
            .IsUnique();

        ConfigureExternalTables(modelBuilder);
    }

    /// <summary>
    /// Maps the two company-owned tables FlowLine only ever *reads* (History as an order source,
    /// Staff_Table as a name lookup). Every table/column is named to match the existing schema,
    /// and every entity is marked <c>ExcludeFromMigrations()</c> so FlowLine's own migrations
    /// never emit DDL against tables it doesn't own — <c>Database.Migrate()</c> stays responsible
    /// only for FlowLine's seven tables. These tables don't exist under the SQLite dev provider;
    /// callers guard on that (the import UI is a SQL Server-only feature).
    /// </summary>
    private static void ConfigureExternalTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HistoryRecord>(e =>
        {
            e.ToTable("History", t => t.ExcludeFromMigrations());
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).HasColumnName("ID");
            e.Property(h => h.OrderId).HasColumnName("OrderId");
            e.Property(h => h.Sku).HasColumnName("SKU");
            e.Property(h => h.Qty).HasColumnName("QTY");
            e.Property(h => h.Channel).HasColumnName("Channel");
            e.Property(h => h.Date).HasColumnName("Date");
            e.Property(h => h.IsTested).HasColumnName("IsTested");
            e.Property(h => h.TestedBy).HasColumnName("TestedBy");
            e.Property(h => h.Status).HasColumnName("Status");
            e.Property(h => h.PackedBy).HasColumnName("PackedBy");
            e.Property(h => h.PackedDate).HasColumnName("PackedDate");
            e.Property(h => h.AssigneeNumber).HasColumnName("Assigne Number");
        });

        modelBuilder.Entity<StaffMember>(e =>
        {
            e.ToTable("Staff_Table", t => t.ExcludeFromMigrations());
            e.HasKey(s => s.StaffNumber);
            e.Property(s => s.StaffNumber).HasColumnName("Staff number").ValueGeneratedNever();
            e.Property(s => s.Name).HasColumnName("Name");
            e.Property(s => s.TestingPower).HasColumnName("Testing Power");
        });
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
