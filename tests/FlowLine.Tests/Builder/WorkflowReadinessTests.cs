using FlowLine.Application.Builder;
using FlowLine.Domain.Entities;

namespace FlowLine.Tests.Builder;

public class WorkflowReadinessTests
{
    private static Stage StageWith(string name, int steps, int stations)
    {
        var stage = new Stage { Name = name, OrderIndex = 0 };
        for (var i = 0; i < steps; i++) stage.Steps.Add(new Step { Name = $"s{i}", OrderIndex = i });
        for (var i = 0; i < stations; i++) stage.Stations.Add(new Station { Name = $"st{i}" });
        return stage;
    }

    [Fact]
    public void Runnable_WhenActive_EveryStageHasStepsAndStations()
    {
        var wf = new Workflow { Name = "WF", IsActive = true };
        wf.Stages.Add(StageWith("A", steps: 2, stations: 1));
        wf.Stages.Add(StageWith("B", steps: 1, stations: 2));

        var r = WorkflowReadiness.For(wf);

        Assert.True(r.IsRunnable);
        Assert.Empty(r.Problems);
    }

    [Fact]
    public void NotRunnable_NoStages()
    {
        var r = WorkflowReadiness.For(new Workflow { Name = "WF", IsActive = true });

        Assert.False(r.IsRunnable);
        Assert.Contains(r.Problems, p => p.Contains("No stages"));
    }

    [Fact]
    public void NotRunnable_StageMissingStationOrSteps_ListsBothProblems()
    {
        var wf = new Workflow { Name = "WF", IsActive = true };
        wf.Stages.Add(StageWith("A", steps: 0, stations: 0));

        var r = WorkflowReadiness.For(wf);

        Assert.False(r.IsRunnable);
        Assert.Contains(r.Problems, p => p.Contains("\"A\"") && p.Contains("no steps"));
        Assert.Contains(r.Problems, p => p.Contains("\"A\"") && p.Contains("no station"));
    }

    [Fact]
    public void NotRunnable_WhenArchived()
    {
        var wf = new Workflow { Name = "WF", IsActive = false };
        wf.Stages.Add(StageWith("A", steps: 1, stations: 1));

        var r = WorkflowReadiness.For(wf);

        Assert.False(r.IsRunnable);
        Assert.Contains(r.Problems, p => p.Contains("archived"));
    }
}
