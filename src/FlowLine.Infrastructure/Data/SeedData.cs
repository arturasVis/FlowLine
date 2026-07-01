using FlowLine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowLine.Infrastructure.Data;

/// <summary>
/// Seeds the original 4-stage gaming-PC build as a Workflow, proving the
/// template/instance model (PRD §6, M1). Idempotent — does nothing if any
/// Workflow already exists.
/// </summary>
public static class SeedData
{
    public static async Task SeedAsync(FlowLineDbContext context)
    {
        // On SQLite dev the company-owned Staff_Table/History don't exist (they're
        // ExcludeFromMigrations, and live only in the real SQL Server). Seed mock copies so
        // login, roles, import, and prebuild scanning are all exercisable locally.
        await SeedDevExternalTablesAsync(context);

        if (await context.Workflows.AnyAsync())
        {
            return;
        }

        var workflow = new Workflow
        {
            Name = "Gaming PC Build",
            Description = "Full assembly of a gaming desktop from bare case to powered-on system.",
            IsActive = true,
        };

        AddStage(workflow, 0, "Case Prep",
        [
            ("Unbox case & remove side panels", "Unbox the case and set both side panels aside."),
            ("Install power supply", "Mount the PSU in its bay, fan facing the vent, and screw it in."),
            ("Install motherboard standoffs", "Install brass standoffs matching the motherboard's mounting holes."),
        ]);

        AddStage(workflow, 1, "Motherboard & CPU",
        [
            ("Install CPU", "Lift the socket lever, align the CPU's notch, lower it in, and close the lever."),
            ("Install CPU cooler", "Apply thermal paste if not pre-applied, then mount the cooler per its bracket."),
            ("Install RAM", "Open the DIMM slot clips and seat the RAM sticks in the recommended slots until they click."),
            ("Mount motherboard in case", "Lower the motherboard onto the standoffs and secure with screws."),
        ]);

        AddStage(workflow, 2, "Storage & Expansion",
        [
            ("Install storage", "Seat the M.2 drive in its slot and secure with the retention screw, or mount the SATA SSD in a drive bay."),
            ("Install graphics card", "Remove the matching rear slot covers, seat the GPU in the top PCIe x16 slot, and secure it."),
            ("Connect power & data cables", "Connect the 24-pin, CPU power, GPU power, and any SATA/data cables."),
        ]);

        AddStage(workflow, 3, "Cable Management & Final Test",
        [
            ("Route and tie down cables", "Route cables behind the motherboard tray and secure with zip ties."),
            ("Connect front panel I/O", "Connect the power button, reset button, LEDs, and front USB/audio headers."),
            ("Power on and verify boot", "Power on the system and confirm it reaches BIOS/POST successfully."),
        ]);

        // One station per stage — the relay's default capacity model (PRD §6.5).
        foreach (var stage in workflow.Stages)
        {
            context.Stations.Add(new Station { Stage = stage, Name = $"Station {stage.OrderIndex + 1}" });
        }

        context.Workflows.Add(workflow);
        await context.SaveChangesAsync();

        // A few demo units queued at the first stage so the station screen has something
        // to claim immediately, rather than showing an empty line on first run.
        var firstStage = workflow.Stages.OrderBy(s => s.OrderIndex).First();
        var now = DateTime.UtcNow;
        context.WorkItems.AddRange(
            NewWorkItem(workflow, firstStage, "ORD-1001", "GPU-RTX4070-OC", now),
            NewWorkItem(workflow, firstStage, "ORD-1002", "GPU-RTX4060-STD", now.AddMinutes(1)),
            NewWorkItem(workflow, firstStage, "ORD-1003", "GPU-RTX4070-OC", now.AddMinutes(2)));
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// SQLite-only: creates and seeds mock <c>Staff_Table</c> and <c>History</c> (the tables the
    /// real deployment reads from the company SQL Server). Idempotent — CREATE TABLE IF NOT EXISTS
    /// plus insert-only-when-empty. Column names match the HasColumnName mapping in
    /// FlowLineDbContext (spaces included). No-ops on SQL Server, where the real tables exist.
    /// </summary>
    private static async Task SeedDevExternalTablesAsync(FlowLineDbContext context)
    {
        if (!context.Database.IsSqlite())
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "Staff_Table" (
                "Staff number" INTEGER NOT NULL PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Testing Power" INTEGER NULL
            );
            """);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "History" (
                "ID" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                "OrderId" TEXT NOT NULL,
                "SKU" TEXT NOT NULL,
                "QTY" INTEGER NOT NULL,
                "Channel" TEXT NULL,
                "Date" TEXT NOT NULL,
                "IsTested" INTEGER NOT NULL,
                "TestedBy" TEXT NULL,
                "Status" TEXT NULL,
                "PackedBy" TEXT NULL,
                "PackedDate" TEXT NULL,
                "Assigne Number" INTEGER NULL
            );
            """);

        if (!await context.Staff.AnyAsync())
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Staff_Table" ("Staff number", "Name", "Testing Power") VALUES
                    (1001, 'Alex Assembler', 1),
                    (1002, 'Bailey Bench', 2),
                    (1003, 'Morgan Manager', 3),
                    (1004, 'Sam Solderer', 1);
                """);
        }

        if (!await context.History.AnyAsync())
        {
            // A mix of importable orders (ORD-####) and scannable prebuilds (PB-####). The scanned
            // prebuild ID matches History.OrderId. Dates are ISO strings (SQLite stores DateTime as text).
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "History" ("OrderId", "SKU", "QTY", "Channel", "Date", "IsTested", "Status", "Assigne Number") VALUES
                    ('ORD-2001', 'CPU-RYZEN-9950X', 2, 'eBay',     '2026-07-01 08:15:00', 0, 'Pending', 1001),
                    ('ORD-2002', 'RAM-DDR5-32GB',   4, 'Shopify',  '2026-07-01 09:30:00', 0, 'Pending', 1002),
                    ('ORD-2003', 'SSD-NVME-2TB',    1, 'Amazon',   '2026-07-01 10:45:00', 0, 'Pending', 1004),
                    ('PB-5001',  'GPU-RTX4070-PREBUILT', 1, NULL,  '2026-06-29 12:00:00', 1, 'Prebuilt', NULL),
                    ('PB-5002',  'AIO-RADIATOR-360-ASSY', 1, NULL, '2026-06-29 13:20:00', 1, 'Prebuilt', NULL);
                """);
        }
    }

    private static WorkItem NewWorkItem(Workflow workflow, Stage firstStage, string orderNumber, string sku, DateTime queuedAtUtc) =>
        new()
        {
            Workflow = workflow,
            CurrentStage = firstStage,
            OrderNumber = orderNumber,
            Sku = sku,
            Quantity = 1,
            Channel = "Retail",
            QueuedAtUtc = queuedAtUtc,
        };

    private static void AddStage(Workflow workflow, int orderIndex, string name, (string Name, string Instructions)[] steps)
    {
        var stage = new Stage
        {
            Workflow = workflow,
            Name = name,
            OrderIndex = orderIndex,
        };
        workflow.Stages.Add(stage);

        for (var i = 0; i < steps.Length; i++)
        {
            stage.Steps.Add(new Step
            {
                Stage = stage,
                Name = steps[i].Name,
                Instructions = steps[i].Instructions,
                OrderIndex = i,
            });
        }
    }
}
