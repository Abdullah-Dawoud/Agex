using System.Text;
using System.Text.Json;
using Agex.Core.Runtime;

namespace Agex.Core.Sessions;

public sealed record SearchHit(string SessionId, string SessionTitle, string Project, string Where, string Snippet, DateTimeOffset At);

/// <summary>
/// Session history on disk: one JSON file per session plus a small index for
/// fast listing. Everything stays local.
/// </summary>
public sealed class SessionStore
{
    private readonly string _root;
    private readonly AgexLog? _log;
    private readonly object _lock = new();

    public SessionStore(string root, AgexLog? log = null)
    {
        _root = root;
        _log = log;
    }

    private string IndexPath => Path.Combine(_root, "index.json");
    private string FileFor(string id) => Path.Combine(_root, SafeId(id) + ".json");

    private static string SafeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80 || id.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
            throw new ArgumentException("Invalid session id.");
        return id;
    }

    public void Save(Session session)
    {
        lock (_lock)
        {
            session.UpdatedAt = DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(session.Title)) session.Title = MakeTitle(session.Request);
            try
            {
                Json.WriteFile(FileFor(session.Id), session);
                var index = ReadIndex();
                index.RemoveAll(item => item.Id == session.Id);
                index.Add(ToSummary(session));
                WriteIndex(index);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // History must never break a running request (full disk, locked file).
                _log?.Error("session_save_failed", ex, new { session = session.Id });
            }
        }
    }

    public Session? Load(string id)
    {
        try { return Json.ReadFile<Session>(FileFor(id)); }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException)
        {
            _log?.Error("session_load_failed", ex, new { session = id });
            return null;
        }
    }

    public IReadOnlyList<SessionSummary> List(string? project = null)
    {
        lock (_lock)
        {
            var index = ReadIndex();
            return index.Where(item => project is null || SamePath(item.Project, project)).OrderByDescending(item => item.CreatedAt).ToList();
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            var file = FileFor(id);
            var existed = File.Exists(file);
            if (existed) File.Delete(file);
            var index = ReadIndex();
            if (index.RemoveAll(item => item.Id == id) > 0) WriteIndex(index);
            return existed;
        }
    }

    /// <summary>Deletes sessions older than the retention period or beyond the maximum count. Running sessions are kept.</summary>
    public int ApplyRetention(int retentionDays, int maxSessions)
    {
        lock (_lock)
        {
            var index = ReadIndex().OrderByDescending(item => item.CreatedAt).ToList();
            var cutoff = retentionDays > 0 ? DateTimeOffset.UtcNow.AddDays(-retentionDays) : DateTimeOffset.MinValue;
            var remove = index.Where((item, position) => !SessionStatusText.IsActive(item.Status) && (item.CreatedAt < cutoff || position >= maxSessions)).ToList();
            foreach (var item in remove)
            {
                try { File.Delete(FileFor(item.Id)); } catch (Exception ex) when (ex is IOException or ArgumentException) { }
            }
            if (remove.Count > 0) WriteIndex(index.Except(remove).ToList());
            return remove.Count;
        }
    }

    /// <summary>Marks sessions left "running" by a crash as interrupted. They are never restarted automatically.</summary>
    public IReadOnlyList<string> MarkInterrupted()
    {
        var interrupted = new List<string>();
        foreach (var summary in List().Where(item => SessionStatusText.IsActive(item.Status)))
        {
            if (Load(summary.Id) is not { } session) continue;
            // Another AGEX process (the desktop app or a terminal) may be running it right now.
            if (IsOwnerAlive(session)) continue;
            session.Status = SessionStatus.Interrupted;
            session.Timeline.Add(new TimelineEntry { Kind = TimelineKind.Warning, Text = "AGEX stopped while this request was running. Nothing was restarted automatically." });
            foreach (var task in session.Tasks.Where(task => task.State is TaskState.Queued or TaskState.Waiting or TaskState.Starting or TaskState.Running or TaskState.Verifying))
            {
                task.State = TaskState.Cancelled;
                task.Error = "Interrupted: AGEX stopped.";
            }
            Save(session);
            interrupted.Add(session.Id);
        }
        return interrupted;
    }

    private static bool IsOwnerAlive(Session session)
    {
        if (session.OwnerPid <= 0) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(session.OwnerPid);
            if (process.HasExited) return false;
            return Math.Abs((new DateTimeOffset(process.StartTime) - session.OwnerStarted).TotalSeconds) < 2;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return false; }
    }

    /// <summary>Local full-text search over session requests, messages and results.</summary>
    public IReadOnlyList<SearchHit> Search(string query, int maxHits = 100)
    {
        var hits = new List<SearchHit>();
        if (string.IsNullOrWhiteSpace(query)) return hits;
        foreach (var summary in List())
        {
            if (hits.Count >= maxHits) break;
            if (Load(summary.Id) is not { } session) continue;
            void Check(string where, string text, DateTimeOffset at)
            {
                var position = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (position < 0 || hits.Count >= maxHits) return;
                var start = Math.Max(0, position - 60);
                var end = Math.Min(text.Length, position + query.Length + 60);
                var snippet = (start > 0 ? "..." : "") + text[start..end].Replace('\n', ' ') + (end < text.Length ? "..." : "");
                hits.Add(new SearchHit(session.Id, session.Title, session.Project, where, snippet, at));
            }
            Check("Request", session.Request, session.CreatedAt);
            foreach (var message in session.Messages) Check($"{message.From} to {message.To}", message.Text, message.At);
            if (session.Outcome is { } outcome) Check("Result", string.Join("\n", outcome.Results.Prepend(outcome.Reason)), session.UpdatedAt);
        }
        return hits;
    }

    public static string ExportMarkdown(Session session)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {session.Title}").AppendLine();
        builder.AppendLine($"- Project: {session.Project}");
        builder.AppendLine($"- Started: {session.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        builder.AppendLine($"- Status: {SessionStatusText.Label(session.Status)}");
        builder.AppendLine($"- Agents: {string.Join(", ", session.Agents)}").AppendLine();
        builder.AppendLine("## Request").AppendLine().AppendLine(session.Request).AppendLine();
        if (session.Outcome is { } outcome)
        {
            builder.AppendLine("## Result").AppendLine().AppendLine(outcome.Headline);
            if (outcome.Reason.Length > 0) builder.AppendLine().AppendLine(outcome.Reason);
            if (outcome.Verification.Length > 0) builder.AppendLine().AppendLine("Verification: " + outcome.Verification);
            foreach (var line in outcome.WhatHappened) builder.AppendLine("- " + line);
            builder.AppendLine();
        }
        if (session.Tasks.Count > 0)
        {
            builder.AppendLine("## Tasks").AppendLine();
            foreach (var task in session.Tasks) builder.AppendLine($"- {task.Id} ({task.Agent}): {task.Label} - {task.State}");
            builder.AppendLine();
        }
        builder.AppendLine("## Timeline").AppendLine();
        foreach (var entry in session.Timeline) builder.AppendLine($"- {entry.At.ToLocalTime():HH:mm:ss} {entry.Text}");
        if (session.Changes.Count > 0)
        {
            builder.AppendLine().AppendLine("## Changed files").AppendLine();
            foreach (var change in session.Changes) builder.AppendLine($"- {change.Kind}: {change.Path}");
        }
        return Redactor.Redact(builder.ToString());
    }

    public static string MakeTitle(string request)
    {
        var line = request.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "Request";
        return line.Length <= 80 ? line : line[..77] + "...";
    }

    private static SessionSummary ToSummary(Session session) => new()
    {
        Id = session.Id, Title = session.Title, Project = session.Project, Status = session.Status, CreatedAt = session.CreatedAt,
        UpdatedAt = session.UpdatedAt, Agents = session.Agents.ToList(), Tasks = session.Tasks.Count, Messages = session.Messages.Count,
    };

    private List<SessionSummary> ReadIndex()
    {
        try
        {
            if (File.Exists(IndexPath)) return JsonSerializer.Deserialize<List<SessionSummary>>(File.ReadAllText(IndexPath), Json.Options) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _log?.Error("session_index_damaged", ex);
        }
        return RebuildIndex();
    }

    /// <summary>Rebuilds the index from the session files (used by Repair and after damage).</summary>
    public List<SessionSummary> RebuildIndex()
    {
        var list = new List<SessionSummary>();
        if (!Directory.Exists(_root)) return list;
        foreach (var file in Directory.GetFiles(_root, "*.json"))
        {
            if (Path.GetFileName(file) == "index.json") continue;
            try
            {
                var session = JsonSerializer.Deserialize<Session>(File.ReadAllText(file), Json.Options);
                if (session is { SchemaVersion: >= 2 } && session.Id.Length > 0) list.Add(ToSummary(session));
            }
            catch (Exception ex) when (ex is JsonException or IOException) { }
        }
        WriteIndex(list);
        return list;
    }

    private void WriteIndex(List<SessionSummary> index)
    {
        try { Json.WriteFile(IndexPath, index.OrderByDescending(item => item.CreatedAt).ToList()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.Error("session_index_save_failed", ex); }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
