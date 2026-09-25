using System.Text.RegularExpressions;
using Agex.Core.Attachments;
using Agex.Core.Settings;

namespace Agex.Core.Connections;

public enum TipAction { AddSkill, SetEfficiency, Connect, UseTeam }

/// <summary>One optional suggestion shown above the composer. The user can apply it or dismiss it for good.</summary>
public sealed record Tip(string Id, string Text, TipAction Action, string Argument, string ButtonLabel);

public sealed record TipContext
{
    public string Request { get; init; } = "";
    public IReadOnlyList<AttachmentKind> Attachments { get; init; } = [];
    public string? TeamId { get; init; }
    /// <summary>Skills that will be used for this request.</summary>
    public IReadOnlySet<string> ActiveSkills { get; init; } = new HashSet<string>();
    /// <summary>Skills installed on this computer (enabled or not).</summary>
    public IReadOnlySet<string> InstalledSkills { get; init; } = new HashSet<string>();
    public int ProjectFiles { get; init; }
    public bool OllamaReady { get; init; }
    public EfficiencyMode Efficiency { get; init; } = EfficiencyMode.Balanced;
    public IReadOnlyList<ConnectionItem> TeamConnections { get; init; } = [];
    public IReadOnlyCollection<string> Dismissed { get; init; } = [];
}

/// <summary>
/// Anticipates obvious next steps from what the user is doing. Rules are simple
/// and visible; at most two tips show at once, and a dismissed tip never returns.
/// </summary>
public static partial class Recommendations
{
    public const int LargeProjectFiles = 3000;

    public static IReadOnlyList<Tip> For(TipContext context, int max = 2)
    {
        var tips = new List<Tip>();
        void Add(Tip tip) { if (!context.Dismissed.Contains(tip.Id) && tips.All(existing => existing.Id != tip.Id)) tips.Add(tip); }
        bool Active(string id) => context.ActiveSkills.Contains(id);
        string Verb(string id) => context.InstalledSkills.Contains(id) ? "Add" : "Install";

        // A program the team needs is on this computer but not connected.
        foreach (var connection in context.TeamConnections.Where(item => item.State == ConnectionState.AvailableToConnect && item.Actions.Any(action => action.Kind == ConnectionActionKind.ConnectBridge)))
            Add(new Tip($"connect:{connection.Id}", $"{connection.Name} is on this computer. Connect it so the team can use it?", TipAction.Connect, connection.Id, "Connect"));

        if (context.Attachments.Contains(AttachmentKind.Pdf) && !Active("pdf-documents"))
            Add(new Tip("skill:pdf", "You attached a PDF. The PDF Documents skill reads drawings and forms more reliably.", TipAction.AddSkill, "pdf-documents", Verb("pdf-documents") + " PDF skill"));

        var text = context.Request;
        if (Research().IsMatch(text) && !Active("web-fetch") && !Active("exa-search"))
            Add(new Tip("skill:research", "This looks like research. Turn on web research so agents can read current sources?", TipAction.AddSkill, context.InstalledSkills.Contains("exa-search") || !context.InstalledSkills.Contains("web-fetch") ? "exa-search" : "web-fetch", "Enable web research"));

        if (Code().IsMatch(text) && !Active("requesting-code-review") && !Active("test-driven-development"))
            Add(new Tip("skill:code", "Changing code: Code Review and Test-Driven Development catch mistakes before you see them.", TipAction.AddSkill, "requesting-code-review,test-driven-development", "Add both"));

        if (context.ProjectFiles >= LargeProjectFiles && context.Efficiency != EfficiencyMode.SaveTokens)
            Add(new Tip("efficiency:large", $"Large project ({context.ProjectFiles:N0}+ files). Save tokens mode keeps agents' context small.", TipAction.SetEfficiency, nameof(EfficiencyMode.SaveTokens), "Save tokens"));

        if (context.OllamaReady && context.Efficiency == EfficiencyMode.Balanced)
            Add(new Tip("efficiency:local", "A local model is ready. Local-first gives it the work it can do, which saves cost and keeps data here.", TipAction.SetEfficiency, nameof(EfficiencyMode.LocalFirst), "Use local first"));

        return tips.Take(max).ToList();
    }

    [GeneratedRegex(@"\b(research|competitor|competitors|compare|comparison|market|sources|find out|look up|investigate|trends?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Research();

    [GeneratedRegex(@"\b(fix|bug|implement|refactor|feature|function|class|method|unit tests?|compile|build error|endpoint|api)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Code();
}
