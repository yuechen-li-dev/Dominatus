namespace Dominatus.Actuators.SemanticKernel;

public static class SemanticKernelMicrosoftGraphProfiles
{
    public static SemanticKernelCapabilityProfile OutlookMailCalendar(string pluginPrefix = "graph")
    {
        var prefix = ValidatePluginPrefix(pluginPrefix);

        return new SemanticKernelCapabilityProfile(
            "microsoft-graph.outlook-mail-calendar",
            "Microsoft Graph Outlook Mail/Calendar",
            [
                new($"{prefix}.mail", "list_messages", SemanticKernelCapabilityRisk.Read, "List Outlook mail message headers or summaries."),
                new($"{prefix}.mail", "read_message", SemanticKernelCapabilityRisk.Read, "Read a specific Outlook mail message."),
                new($"{prefix}.mail", "create_draft", SemanticKernelCapabilityRisk.Write, "Create a draft mail message without sending it."),
                new($"{prefix}.mail", "send_message", SemanticKernelCapabilityRisk.ExternalEffect, "Send an Outlook mail message.", RequiresHumanApproval: true),
                new($"{prefix}.calendar", "list_events", SemanticKernelCapabilityRisk.Read, "List Outlook calendar events."),
                new($"{prefix}.calendar", "create_event", SemanticKernelCapabilityRisk.Write, "Create a calendar event.", RequiresHumanApproval: true),
                new($"{prefix}.calendar", "update_event", SemanticKernelCapabilityRisk.Write, "Update a calendar event.", RequiresHumanApproval: true),
                new($"{prefix}.calendar", "cancel_event", SemanticKernelCapabilityRisk.Destructive, "Cancel/delete a calendar event.", RequiresHumanApproval: true)
            ]);
    }

    public static IReadOnlyList<AllowedSemanticKernelFunction> OutlookMailCalendarReadAllowlist(string pluginPrefix = "graph")
        => OutlookMailCalendar(pluginPrefix).ToAllowedFunctions(SemanticKernelCapabilityProfilePredicates.IsReadOnly);

    public static IReadOnlyList<AllowedSemanticKernelFunction> OutlookMailCalendarWriteAllowlist(string pluginPrefix = "graph")
        => OutlookMailCalendar(pluginPrefix).ToAllowedFunctions(e => e.Risk == SemanticKernelCapabilityRisk.Write);

    public static IReadOnlyList<AllowedSemanticKernelFunction> OutlookMailCalendarExternalEffectAllowlist(string pluginPrefix = "graph")
        => OutlookMailCalendar(pluginPrefix).ToAllowedFunctions(e => e.Risk == SemanticKernelCapabilityRisk.ExternalEffect);

    private static string ValidatePluginPrefix(string pluginPrefix)
    {
        if (string.IsNullOrWhiteSpace(pluginPrefix))
            throw new ArgumentException("pluginPrefix is required.", nameof(pluginPrefix));

        var trimmed = pluginPrefix.Trim().TrimEnd('.');
        if (trimmed.Length == 0)
            throw new ArgumentException("pluginPrefix is required.", nameof(pluginPrefix));
        if (trimmed.Any(char.IsWhiteSpace))
            throw new ArgumentException("pluginPrefix cannot contain whitespace.", nameof(pluginPrefix));

        return trimmed;
    }
}
