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
