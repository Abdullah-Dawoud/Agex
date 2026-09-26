using Agex.Core.Agents;

namespace Agex.Core.Sessions;

public enum SessionStatus
{
    Running, WaitingForInput, WaitingForApproval, Paused,
    Complete, CompleteWithFallback, Partial, Failed, StartFailed, Unverified, Cancelled,
    /// <summary>AGEX stopped (crash, power loss) while the session was running.</summary>
    Interrupted,
}

public static class SessionStatusText
{
    public static bool IsActive(SessionStatus status) => status is SessionStatus.Running or SessionStatus.WaitingForInput or SessionStatus.WaitingForApproval or SessionStatus.Paused;

    public static string Label(SessionStatus status) => status switch
    {
        SessionStatus.Running => "Running",
        SessionStatus.WaitingForInput => "Needs your answer",
        SessionStatus.WaitingForApproval => "Needs your approval",
        SessionStatus.Paused => "Paused",
        SessionStatus.Complete => "Complete",
        SessionStatus.CompleteWithFallback => "Complete (recovered)",
        SessionStatus.Partial => "Partly complete",
        SessionStatus.Failed => "Failed",
        SessionStatus.StartFailed => "Could not start",
        SessionStatus.Unverified => "Not verified",
        SessionStatus.Cancelled => "Cancelled",
        SessionStatus.Interrupted => "Interrupted",
        _ => status.ToString(),
    };

    public static string Code(SessionStatus status) => status switch
    {
        SessionStatus.CompleteWithFallback => "COMPLETE_WITH_FALLBACK",
        SessionStatus.StartFailed => "START_FAILED",
        SessionStatus.WaitingForInput => "WAITING_FOR_INPUT",
        SessionStatus.WaitingForApproval => "WAITING_FOR_APPROVAL",
        _ => status.ToString().ToUpperInvariant(),
    };
}

public enum MessageType { Assignment, Question, Answer, Result, Review, Revision, Status, ToolEvent, System }

/// <summary>
/// One explicit message between participants (User, AGEX, agents). Built only
/// from what an agent actually returned or what AGEX actually did; never from
/// hidden model reasoning.
/// </summary>
public sealed class AgentMessage
{
    public long Seq { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public MessageType Type { get; set; }
    public string Text { get; set; } = "";
    public string TaskId { get; set; } = "";
}

public enum TimelineKind { Info, Start, Done, Failed, Fallback, Input, Approval, Warning }

public sealed class TimelineEntry
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public TimelineKind Kind { get; set; }
    public string Text { get; set; } = "";
    public string TaskId { get; set; } = "";
}

public enum TaskState { Queued, Waiting, Starting, Running, Verifying, Done, RepairRequired, Failed, Cancelled, Skipped }

public sealed class TaskItem
{
    public string Id { get; set; } = "";
    /// <summary>Id the leader used, kept so later plans can refer to it.</summary>
    public string OriginalId { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public string Title { get; set; } = "";
    public string Objective { get; set; } = "";
    public string Agent { get; set; } = "";
    public TaskState State { get; set; } = TaskState.Queued;
    public List<string> Dependencies { get; set; } = [];
    public List<string> AffectedFiles { get; set; } = [];
    public List<string> RepairFor { get; set; } = [];
    public string Result { get; set; } = "";
    public string Error { get; set; } = "";
    public string Note { get; set; } = "";
    public string Verification { get; set; } = "";
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? Ended { get; set; }
    public int Attempts { get; set; }
    public UsageReport? Usage { get; set; }
    public bool NeedsWrite => AffectedFiles.Count > 0;
    public string Label => Title.Length > 0 ? Title : Id;
}

/// <summary>Details of one agent run, shown under Diagnostics ("what ran").</summary>
public sealed class RunRecord
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Agent { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string CommandLine { get; set; } = "";
    public int Pid { get; set; }
    public int? ExitCode { get; set; }
    public string Outcome { get; set; } = "";
    public string Reason { get; set; } = "";
    public double Seconds { get; set; }
    public UsageReport? Usage { get; set; }
}

public enum ArtifactKind { File, Report, Patch, Snapshot }

public sealed class Artifact
{
    public ArtifactKind Kind { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Absolute path (files inside the project, reports inside the AGEX data folder).</summary>
    public string Path { get; set; } = "";
    public string Detail { get; set; } = "";
    public string TaskId { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FileChange
{
    public string Path { get; set; } = "";
    /// <summary>added, modified, deleted or renamed.</summary>
    public string Kind { get; set; } = "";
    /// <summary>Lines added and removed; null when AGEX could not compare (binary or very large files).</summary>
    public int? Added { get; set; }
    public int? Removed { get; set; }
    /// <summary>Previous path of a renamed file.</summary>
    public string OldPath { get; set; } = "";
}

public sealed class Outcome
{
    public string Headline { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Verification { get; set; } = "";
    public List<string> WhatHappened { get; set; } = [];
    public List<string> Results { get; set; } = [];
    public int Tasks { get; set; }
    public int Done { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
    public double Seconds { get; set; }
    public string PrimaryFailure { get; set; } = "";
    /// <summary>What AGEX or the user can try next, most useful first.</summary>
    public List<RecoveryOption> Recovery { get; set; } = [];
}

/// <summary>One next step offered after a failure or when a capability is missing. Kind names a <see cref="Agex.Core.Orchestration.RecoveryKind"/>.</summary>
public sealed class RecoveryOption
{
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Argument { get; set; } = "";
}

public sealed class PendingQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string From { get; set; } = "";
    public string Question { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Session
{
    public const int CurrentSchema = 2;
    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Id { get; set; } = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
    public string Project { get; set; } = "";
    public string Request { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public SessionStatus Status { get; set; } = SessionStatus.Running;
    public string Leader { get; set; } = "";
    public string Team { get; set; } = "";
    public List<string> Agents { get; set; } = [];
    public List<string> Skills { get; set; } = [];
    public string ClonedFrom { get; set; } = "";
    public string ContinuedFrom { get; set; } = "";
    /// <summary>How AGEX handled the request: chat, ask, plan or build.</summary>
    public string Mode { get; set; } = "build";
    /// <summary>The composer mode the user chose (auto, ask, plan, build).</summary>
    public string ChosenMode { get; set; } = "";
    /// <summary>Capabilities the request needed, in plain words.</summary>
    public List<string> Needs { get; set; } = [];
    public string Efficiency { get; set; } = "";
    public List<TaskItem> Tasks { get; set; } = [];
    public List<AgentMessage> Messages { get; set; } = [];
    public List<TimelineEntry> Timeline { get; set; } = [];
    public List<RunRecord> Runs { get; set; } = [];
    public List<Artifact> Artifacts { get; set; } = [];
    public List<FileChange> Changes { get; set; } = [];
    public Dictionary<string, UsageReport> Usage { get; set; } = new();
    public List<string> Answers { get; set; } = [];
    public PendingQuestion? Question { get; set; }
    public string SnapshotRef { get; set; } = "";
    public Outcome? Outcome { get; set; }
    /// <summary>Process running this session; lets another AGEX process tell a live session from a crashed one.</summary>
    public int OwnerPid { get; set; }
    public DateTimeOffset OwnerStarted { get; set; }
}

/// <summary>Small record kept in the session index for fast listing.</summary>
public sealed class SessionSummary
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Project { get; set; } = "";
    public SessionStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<string> Agents { get; set; } = [];
    public int Tasks { get; set; }
    public int Messages { get; set; }
    public string Mode { get; set; } = "";
    public string ContinuedFrom { get; set; } = "";
    public string Efficiency { get; set; } = "";
    /// <summary>Usage per agent id, as the agents reported it.</summary>
    public Dictionary<string, Agex.Core.Agents.UsageReport> Usage { get; set; } = new();
}
