namespace FlowLine.Application.Builder;

/// <summary>
/// Where uploaded Step media (PRD FR-4) lands on the local filesystem. The Web project
/// points this at its wwwroot/media so files are servable by static-file middleware
/// without a separate file-serving endpoint — but the Application layer here only knows
/// "a root directory," not that it happens to be wwwroot.
/// </summary>
public class MediaStorageOptions
{
    public required string RootPath { get; init; }
}
