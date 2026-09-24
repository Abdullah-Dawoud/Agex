using System.IO.Compression;
using System.Text;
using System.Xml;
using Agex.Core.Agents;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Attachments;

public enum AttachmentKind { Image, Pdf, Office, Text, Archive, Video, Unsupported }

/// <summary>
/// A file the user attached to a request, copied into AGEX's own attachment
/// folder for that request. <see cref="ExtractedTextPath"/> and
/// <see cref="FramesFolder"/> hold readable conversions made by AGEX.
/// </summary>
public sealed record Attachment
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public AttachmentKind Kind { get; init; }
    public long Size { get; init; }
    /// <summary>Plain-text version (Office documents, zip listings, video metadata).</summary>
    public string? ExtractedTextPath { get; init; }
    /// <summary>Still frames extracted from a video (only when the user allowed it and ffmpeg is installed).</summary>
    public string? FramesFolder { get; init; }
    /// <summary>What AGEX did to the file, in plain words (shown to the user and to agents).</summary>
    public string Note { get; init; } = "";
}

/// <summary>How one agent receives the attachments, and what the user should know.</summary>
public sealed record AttachmentDelivery(string AgentName, IReadOnlyList<string> Lines, bool LeavesComputer, string Destination);

/// <summary>
/// Classifies, copies and converts attached files, and explains per agent how
/// each file reaches it. Nothing is sent that the user did not attach, and
/// unsupported binary files are never passed to agents.
/// </summary>
public sealed class AttachmentService(IPlatformService platform, ProcessRunner runner, AgexLog? log = null)
{
    public const long MaxFileBytes = 100L * 1024 * 1024;
    public const int MaxFiles = 20;
    /// <summary>Text up to this size is given inline to agents that cannot open files (Ollama).</summary>
    public const int InlineTextLimit = 60_000;

    private static readonly HashSet<string> ImageExt = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];
    private static readonly HashSet<string> OfficeExt = [".docx", ".xlsx", ".pptx"];
    private static readonly HashSet<string> VideoExt = [".mp4", ".mov", ".webm", ".mkv", ".avi"];
    private static readonly HashSet<string> TextExt =
    [
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".jsonl", ".xml", ".yaml", ".yml", ".toml", ".ini", ".log", ".html", ".htm", ".css", ".scss",
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".py", ".rb", ".php", ".java", ".kt", ".kts", ".swift", ".go", ".rs", ".c", ".h", ".cpp", ".hpp", ".cc",
        ".cs", ".csproj", ".sln", ".slnx", ".fs", ".vb", ".sql", ".sh", ".ps1", ".psm1", ".bat", ".cmd", ".r", ".lua", ".dart", ".vue", ".svelte", ".tex", ".rst",
        ".gradle", ".cmake", ".dockerfile", ".env.example", ".svg", ".lisp", ".lsp", ".scr", ".dxf",
    ];

    public static AttachmentKind Classify(string fileName)
    {
        var name = System.IO.Path.GetFileName(fileName).ToLowerInvariant();
        var ext = System.IO.Path.GetExtension(name);
        if (name is "dockerfile" or "makefile" or "readme" or "license") return AttachmentKind.Text;
        if (ImageExt.Contains(ext)) return AttachmentKind.Image;
        if (ext == ".pdf") return AttachmentKind.Pdf;
        if (OfficeExt.Contains(ext)) return AttachmentKind.Office;
        if (VideoExt.Contains(ext)) return AttachmentKind.Video;
        if (ext == ".zip") return AttachmentKind.Archive;
        if (TextExt.Contains(ext)) return AttachmentKind.Text;
        return AttachmentKind.Unsupported;
    }

    public static string KindLabel(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Image => "Image",
        AttachmentKind.Pdf => "PDF",
        AttachmentKind.Office => "Office document",
        AttachmentKind.Text => "Text or code",
        AttachmentKind.Archive => "Zip archive",
        AttachmentKind.Video => "Video",
        _ => "Not supported",
    };

    public bool CanExtractVideoFrames => platform.FindExecutable("ffmpeg") is not null && platform.FindExecutable("ffprobe") is not null;

    /// <summary>
    /// Copies the files into <paramref name="folder"/> and makes readable
    /// versions where needed. Unsupported files are refused with a reason.
    /// </summary>
    public async Task<(IReadOnlyList<Attachment> Attached, IReadOnlyList<string> Refused)> PrepareAsync(string folder, IEnumerable<string> sources, bool extractVideoFrames, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var attached = new List<Attachment>();
        var refused = new List<string>();
        foreach (var source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var name = System.IO.Path.GetFileName(source);
            if (attached.Count >= MaxFiles) { refused.Add($"{name}: at most {MaxFiles} files per request."); continue; }
            var info = new FileInfo(source);
            if (!info.Exists) { refused.Add($"{name}: file not found."); continue; }
            if (info.Length > MaxFileBytes) { refused.Add($"{name}: larger than {MaxFileBytes / 1024 / 1024} MB."); continue; }
            var kind = Classify(name);
            if (kind == AttachmentKind.Unsupported) { refused.Add($"{name}: this file type is not supported, so it is not sent to agents."); continue; }
            var target = UniquePath(folder, name);
            File.Copy(info.FullName, target);
            var attachment = new Attachment { Path = target, Name = System.IO.Path.GetFileName(target), Kind = kind, Size = info.Length };
            try
            {
                attachment = kind switch
                {
                    AttachmentKind.Office => WithText(attachment, ExtractOfficeText(target), "Text extracted by AGEX from the document"),
                    AttachmentKind.Archive => WithText(attachment, ListZip(target), "List of files inside the zip (the zip is not unpacked)"),
                    AttachmentKind.Video => await PrepareVideoAsync(attachment, extractVideoFrames, cancellationToken).ConfigureAwait(false),
                    _ => attachment,
                };
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or XmlException or UnauthorizedAccessException)
            {
                log?.Error("attachment_conversion_failed", ex, new { kind = kind.ToString() });
                attachment = attachment with { Note = "AGEX could not read its contents; agents get the file only." };
            }
            attached.Add(attachment);
        }
        log?.Write("attachments_prepared", new { count = attached.Count, refused = refused.Count });
        return (attached, refused);
    }

    private static string UniquePath(string folder, string name)
    {
        var safe = string.Concat(name.Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var path = System.IO.Path.Combine(folder, safe);
        for (var index = 2; File.Exists(path); index++)
            path = System.IO.Path.Combine(folder, $"{System.IO.Path.GetFileNameWithoutExtension(safe)} ({index}){System.IO.Path.GetExtension(safe)}");
        return path;
    }

    private static Attachment WithText(Attachment attachment, string text, string note)
    {
        var path = attachment.Path + ".txt";
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return attachment with { ExtractedTextPath = path, Note = note };
    }

    /// <summary>Plain text of a .docx, .xlsx or .pptx file (Open XML), without any external library.</summary>
    public static string ExtractOfficeText(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var builder = new StringBuilder();
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".docx")
        {
            if (zip.GetEntry("word/document.xml") is { } document) AppendParagraphs(document, "p", "t", builder);
        }
        else if (ext == ".pptx")
        {
            var slides = zip.Entries.Where(entry => entry.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal))
                .OrderBy(entry => int.TryParse(new string(entry.Name.Where(char.IsDigit).ToArray()), out var n) ? n : 0);
            foreach (var slide in slides)
            {
                builder.AppendLine($"--- {System.IO.Path.GetFileNameWithoutExtension(slide.Name)} ---");
                AppendParagraphs(slide, "p", "t", builder);
            }
        }
        else if (ext == ".xlsx")
        {
            var shared = new List<string>();
            if (zip.GetEntry("xl/sharedStrings.xml") is { } strings)
            {
                using var reader = XmlReader.Create(strings.Open(), SafeXml);
                var current = new StringBuilder();
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si") current.Clear();
                    else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t") current.Append(reader.ReadElementContentAsString());
                    if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "si") shared.Add(current.ToString());
                }
            }
            foreach (var sheet in zip.Entries.Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)).OrderBy(entry => entry.FullName, StringComparer.Ordinal))
            {
                builder.AppendLine($"--- {System.IO.Path.GetFileNameWithoutExtension(sheet.Name)} ---");
                using var reader = XmlReader.Create(sheet.Open(), SafeXml);
                var row = new List<string>();
                string? type = null;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row") row.Clear();
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c") type = reader.GetAttribute("t");
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "v" or "t")
                    {
                        var value = reader.ReadElementContentAsString();
                        row.Add(type == "s" && int.TryParse(value, out var index) && index < shared.Count ? shared[index] : value);
                    }
                    if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row" && row.Count > 0) builder.AppendLine(string.Join("\t", row));
                    if (builder.Length > 2_000_000) break;
                }
            }
        }
        return builder.ToString();
    }

    // No DTDs and no external resources: attached files are untrusted.
    private static readonly XmlReaderSettings SafeXml = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };

    private static void AppendParagraphs(ZipArchiveEntry entry, string paragraph, string text, StringBuilder builder)
    {
        using var reader = XmlReader.Create(entry.Open(), SafeXml);
        var line = new StringBuilder();
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == text) line.Append(reader.ReadElementContentAsString());
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == paragraph)
            {
                if (line.Length > 0) builder.AppendLine(line.ToString());
                line.Clear();
            }
            if (builder.Length > 2_000_000) break;
        }
        if (line.Length > 0) builder.AppendLine(line.ToString());
    }

    private static string ListZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var builder = new StringBuilder();
        foreach (var entry in zip.Entries.Take(2000)) builder.AppendLine($"{entry.FullName}\t{entry.Length} bytes");
        if (zip.Entries.Count > 2000) builder.AppendLine($"... and {zip.Entries.Count - 2000} more entries");
        return builder.ToString();
    }

    private async Task<Attachment> PrepareVideoAsync(Attachment attachment, bool extractFrames, CancellationToken cancellationToken)
    {
        var ffprobe = platform.FindExecutable("ffprobe");
        var ffmpeg = platform.FindExecutable("ffmpeg");
        if (ffprobe is null || ffmpeg is null)
            return attachment with { Note = "Agents cannot watch videos. Install ffmpeg to let AGEX give them still frames and details." };
        var probe = await runner.RunAsync(new ProcessRequest
        {
            FileName = ffprobe, Arguments = ["-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name,width,height", "-of", "default=noprint_wrappers=1", attachment.Path],
            WorkingDirectory = System.IO.Path.GetDirectoryName(attachment.Path)!, Timeout = TimeSpan.FromSeconds(30), Label = "video details",
        }, cancellationToken).ConfigureAwait(false);
        var metadata = probe.Succeeded ? probe.Stdout : "";
        var withText = WithText(attachment, "Video details (ffprobe):\n" + metadata, "Video details read by AGEX");
        if (!extractFrames) return withText with { Note = "Video details only (frames were not extracted)" };
        var seconds = double.TryParse(metadata.Split('\n').FirstOrDefault(line => line.StartsWith("duration=", StringComparison.Ordinal))?[9..].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        var frames = attachment.Path + ".frames";
        Directory.CreateDirectory(frames);
        // Six evenly spaced frames, scaled down: enough to understand a clip without sending the video.
        var rate = seconds > 0 ? (6 / seconds).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) : "1";
        var run = await runner.RunAsync(new ProcessRequest
        {
            FileName = ffmpeg, Arguments = ["-v", "error", "-i", attachment.Path, "-vf", $"fps={rate},scale=1024:-2", "-frames:v", "6", System.IO.Path.Combine(frames, "frame-%02d.jpg")],
            WorkingDirectory = frames, Timeout = TimeSpan.FromMinutes(2), Label = "video frames",
        }, cancellationToken).ConfigureAwait(false);
        var count = Directory.Exists(frames) ? Directory.GetFiles(frames, "*.jpg").Length : 0;
        return count > 0 && run.Succeeded
            ? withText with { FramesFolder = frames, Note = $"{count} still frames and video details extracted by AGEX (agents cannot watch video)" }
            : withText with { Note = "Video details only; AGEX could not extract frames." };
    }

    /// <summary>Files an agent may read: the attachment itself when useful, plus AGEX's readable versions.</summary>
    public static IEnumerable<string> ReadableFiles(Attachment attachment)
    {
        if (attachment.Kind is AttachmentKind.Image or AttachmentKind.Pdf or AttachmentKind.Text or AttachmentKind.Office) yield return attachment.Path;
        if (attachment.ExtractedTextPath is { } text) yield return text;
        if (attachment.FramesFolder is { } frames && Directory.Exists(frames))
            foreach (var frame in Directory.GetFiles(frames, "*.jpg").Order()) yield return frame;
    }

    /// <summary>The attachments section of a prompt: what the user attached and where the readable files are.</summary>
    public static string PromptSection(IReadOnlyList<Attachment> attachments, bool agentCanOpenFiles)
    {
        if (attachments.Count == 0) return "";
        var builder = new StringBuilder("FILES THE USER ATTACHED (read them when they matter for the task; they are not part of the project folder):\n");
        foreach (var attachment in attachments)
        {
            builder.Append($"- {attachment.Name} ({KindLabel(attachment.Kind)}, {attachment.Size / 1024.0:0.#} KB)");
            if (attachment.Note.Length > 0) builder.Append($" - {attachment.Note}");
            builder.AppendLine();
            if (agentCanOpenFiles)
                foreach (var file in ReadableFiles(attachment)) builder.AppendLine($"    {file}");
        }
        if (!agentCanOpenFiles)
        {
            // Text-only agents get the text itself, within a size limit.
            foreach (var attachment in attachments)
            {
                var textFile = attachment.Kind == AttachmentKind.Text ? attachment.Path : attachment.ExtractedTextPath;
                if (textFile is null || !File.Exists(textFile)) continue;
                var text = File.ReadAllText(textFile);
                builder.AppendLine($"--- {attachment.Name} ---").AppendLine(text.Length > InlineTextLimit ? text[..InlineTextLimit] + "\n[... cut: the file is longer]" : text);
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// What each agent will receive, for the confirmation shown before sending.
    /// Cloud agents are named with the company that receives the files.
    /// </summary>
    public static IReadOnlyList<AttachmentDelivery> Plan(IReadOnlyList<Attachment> attachments, IEnumerable<(IAgentAdapter Adapter, string? Model, bool VisionModel)> members)
    {
        var plans = new List<AttachmentDelivery>();
        foreach (var (adapter, model, vision) in members)
        {
            var lines = new List<string>();
            var canOpen = adapter.Capabilities.Contains(Capability.ReadFiles);
            foreach (var attachment in attachments)
            {
                var line = attachment.Kind switch
                {
                    AttachmentKind.Image when canOpen || vision => $"{attachment.Name}: image",
                    AttachmentKind.Image => $"{attachment.Name}: not sent (this model cannot see images)",
                    AttachmentKind.Pdf when canOpen => $"{attachment.Name}: PDF file",
                    AttachmentKind.Pdf => $"{attachment.Name}: not sent (this agent cannot open PDF files)",
                    AttachmentKind.Office => $"{attachment.Name}: text extracted by AGEX" + (canOpen ? " plus the original file" : ""),
                    AttachmentKind.Text => canOpen ? $"{attachment.Name}: file" : $"{attachment.Name}: text included in the request",
                    AttachmentKind.Archive => $"{attachment.Name}: list of its files only",
                    AttachmentKind.Video when attachment.FramesFolder is not null && (canOpen || vision) => $"{attachment.Name}: still frames and video details",
                    AttachmentKind.Video => $"{attachment.Name}: video details only",
                    _ => $"{attachment.Name}: not sent",
                };
                lines.Add(line);
            }
            var cloud = adapter.PrivacyFor(model) != PrivacyKind.Local;
            plans.Add(new AttachmentDelivery(adapter.Name, lines, cloud, cloud ? adapter.DataDestination(model) : "stays on this computer"));
        }
        return plans;
    }
}
