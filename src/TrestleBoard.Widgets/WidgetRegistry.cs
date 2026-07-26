using System.Diagnostics.CodeAnalysis;

namespace TrestleBoard.Widgets;

/// <summary>
/// In-process widget catalogue (PLAN.md §5 — no runtime plugin loader in v1). Lookup NEVER throws
/// for an unknown id: that is the forward-compatibility path for a newsletter written by a later
/// version (docs/M7-spec.md §2.1).
/// </summary>
public sealed class WidgetRegistry
{
    private readonly Dictionary<string, IWidgetDefinition> _byTypeId = new(StringComparer.Ordinal);
    private readonly List<IWidgetDefinition> _all = [];

    /// <summary>The six built-ins, in <see cref="BuiltInWidgets.All"/> order — also menu order.</summary>
    public static WidgetRegistry CreateDefault()
    {
        var registry = new WidgetRegistry();
        foreach (IWidgetDefinition definition in BuiltInWidgets.All)
        {
            registry.Register(definition);
        }

        return registry;
    }

    /// <summary>A duplicate id is a programming error — caught at startup, never at the user's expense.</summary>
    public void Register(IWidgetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_byTypeId.TryAdd(definition.TypeId, definition))
        {
            throw new InvalidOperationException($"Widget type already registered: {definition.TypeId}");
        }

        _all.Add(definition);
    }

    public bool TryGet(string? typeId, [NotNullWhen(true)] out IWidgetDefinition? definition)
    {
        if (typeId is null)
        {
            definition = null;
            return false;
        }

        return _byTypeId.TryGetValue(typeId, out definition);
    }

    public IReadOnlyList<IWidgetDefinition> All => _all;
}
