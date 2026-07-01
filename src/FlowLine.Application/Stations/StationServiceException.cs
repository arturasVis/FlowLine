namespace FlowLine.Application.Stations;

/// <summary>Thrown for station-management operations that are invalid given current state.</summary>
public class StationServiceException(string message) : Exception(message);
