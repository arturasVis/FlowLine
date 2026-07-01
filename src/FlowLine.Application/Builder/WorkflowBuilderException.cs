namespace FlowLine.Application.Builder;

/// <summary>
/// Thrown for workflow-builder operations that are invalid given current state — including
/// deletes blocked by the database's FK Restrict behavior (e.g. deleting a Stage that still
/// has WorkItems on it, or a Step with recorded StepCompletions). See
/// FlowLineDbContext.OnModelCreating for which relationships are Restrict and why.
/// </summary>
public class WorkflowBuilderException(string message) : Exception(message);
