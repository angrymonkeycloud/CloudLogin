namespace WorkshopUI;

public sealed record WorkshopFeature(string Id, string Title, string Group, string Description, string Code, string[] Instructions, string? Prerequisite = null);
