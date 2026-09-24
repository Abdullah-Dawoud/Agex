using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agex.Core.Platform;

namespace Agex.Core.Agents;

/// <summary>
/// Local models through the Ollama HTTP API (<c>/api/chat</c>, <c>/api/tags</c>).
/// Ollama answers with text only: it cannot open or change project files, so
/// AGEX gives it file contents and never assigns it file edits. AGEX never
/// starts or installs Ollama.
/// </summary>
public sealed partial class OllamaAdapter : IAgentAdapter
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Func<string?> _defaultModel;

    public OllamaAdapter(Func<string?>? defaultModel = null) { _defaultModel = defaultModel ?? (() => null); }

    public string Id => "ollama";
    public string Name => "Ollama";
    public string Provider => "Ollama (local)";
    public string Description => "Private local models for answers, planning and review. Cannot edit files.";
    public AdapterStability Stability => AdapterStability.Beta;
    public IReadOnlySet<Capability> Capabilities { get; } = new HashSet<Capability> { Capability.Planning, Capability.CodeReview, Capability.Documents };
    public bool CanWriteFiles => false;
    public IReadOnlySet<OsKind> SupportedPlatforms { get; } = new HashSet<OsKind> { OsKind.Windows, OsKind.MacOS, OsKind.Linux };
    public int MaxConcurrentRuns => 1;

    /// <summary>Ollama's ":cloud" models run on Ollama's servers, not on this computer.</summary>
    public static bool IsCloudModel(string? model) => model is not null && CloudModel().IsMatch(model);

    [GeneratedRegex(@"(?i)(:|-)cloud\b")]
    private static partial Regex CloudModel();

    public PrivacyKind PrivacyFor(string? model) => model is null ? PrivacyKind.Mixed : IsCloudModel(model) ? PrivacyKind.Cloud : PrivacyKind.Local;
    public string DataDestination(string? model) => IsCloudModel(model) ? "Ollama cloud" : "This computer (Ollama)";

    public static Uri Endpoint()
    {
        var host = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (string.IsNullOrWhiteSpace(host)) return new Uri("http://127.0.0.1:11434/");
        if (!host.Contains("://", StringComparison.Ordinal)) host = "http://" + host;
        var uri = new UriBuilder(host);
        if (uri.Host is "0.0.0.0" or "::" or "[::]") uri.Host = "127.0.0.1";
        if (uri.Port <= 0 || host.EndsWith(uri.Host, StringComparison.OrdinalIgnoreCase)) uri.Port = 11434;
        uri.Path = "/";
        return uri.Uri;
    }

    public AgentDetection Detect(IPlatformService platform)
    {
        var path = ToolLocator.Locate(platform, Id, ["ollama"]);
        return new AgentDetection
        {
            Id = Id, Name = Name, Path = path ?? Endpoint().ToString(),
            Status = path is null ? AgentStatus.NotInstalled : AgentStatus.Available,
            Reason = path is null ? "Ollama is not installed." : "",
        };
    }

    public async Task<AgentDetection> CheckHealthAsync(AgentDetection detection, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var document = await GetJsonAsync("api/version", timeout.Token).ConfigureAwait(false);
            var version = document.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
            var models = await ListModelsAsync(detection, cancellationToken).ConfigureAwait(false);
            if (models.Count == 0)
                return detection with { Status = AgentStatus.Broken, Version = version, Reason = "Ollama is running but has no models. Download one in Ollama (for example: ollama pull qwen2.5-coder:7b).", CheckedAt = DateTimeOffset.UtcNow };
            return detection with { Status = AgentStatus.Supported, Version = version, Reason = "", CheckedAt = DateTimeOffset.UtcNow };
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            return detection with
            {
                Status = detection.Status == AgentStatus.NotInstalled ? AgentStatus.NotInstalled : AgentStatus.Broken,
                Reason = detection.Status == AgentStatus.NotInstalled ? detection.Reason : "Ollama is installed but not running. Start the Ollama app, then check again.",
                CheckedAt = DateTimeOffset.UtcNow,
            };
        }
    }

    public AgentSetupInfo Setup { get; } = new()
    {
        Method = InstallMethod.Manual,
        OfficialUrl = "https://ollama.com/download",
        ManualCommands = new Dictionary<OsKind, string>
        {
            [OsKind.Windows] = "Download and run OllamaSetup.exe from ollama.com/download",
            [OsKind.MacOS] = "Download the Ollama app from ollama.com/download",
            [OsKind.Linux] = "curl -fsSL https://ollama.com/install.sh | sh      (uses sudo)",
        },
        WhatGetsInstalled = "The Ollama app and its local model server. Models are downloaded separately (several GB each).",
        SizeHint = "app about 1 GB; each model several GB",
        AdminNote = "Windows and macOS: your user only. Linux: the official script uses sudo.",
        AccountNote = "Local models need no account and keep everything on this computer. Models named '...:cloud' run on Ollama's servers.",
        LoginArguments = null,
        LoginInstructions = "Local models need no sign-in.",
        LoginDocsUrl = "https://ollama.com/download",
    };

    public bool PassiveAuthCheck => true;

    public Task<AuthCheck> CheckAuthAsync(AgentDetection detection, CancellationToken cancellationToken) =>
        Task.FromResult(new AuthCheck(AuthState.NotRequired, "", "Local"));

    public async Task<ModelDiscovery> GetModelsAsync(AgentDetection detection, CancellationToken cancellationToken)
    {
        const string source = "Ollama /api/tags";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await Http.GetAsync(new Uri(Endpoint(), "api/tags"), timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var models = new List<ModelInfo>();
            // /api/show reports each model's capabilities (vision, tools, thinking) and context length.
            foreach (var model in ParseTags(json)) models.Add(await EnrichAsync(model, cancellationToken).ConfigureAwait(false));
            return ModelDiscovery.From(models, source + " and /api/show", "Ollama is running but has no models. Download one, for example: ollama pull qwen2.5-coder:7b");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return ModelDiscovery.Failed("Ollama is not running. Start the Ollama app, then refresh.", source);
        }
        catch (JsonException) { return ModelDiscovery.Failed("Ollama sent a model list AGEX cannot read.", source); }
    }

    private static async Task<ModelInfo> EnrichAsync(ModelInfo model, CancellationToken cancellationToken)
    {
        try
        {
            var capabilities = await ShowAsync(model.Id, cancellationToken).ConfigureAwait(false);
            return capabilities is { } show ? ApplyShow(model, show) : model;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { return model; }
    }

    private static async Task<string?> ShowAsync(string model, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var response = await Http.PostAsync(new Uri(Endpoint(), "api/show"), JsonContent.Create(new { model }), timeout.Token).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false) : null;
    }

    /// <summary>Adds the capabilities and context length that Ollama's /api/show reports.</summary>
    internal static ModelInfo ApplyShow(ModelInfo model, string showJson)
    {
        using var document = JsonDocument.Parse(showJson);
        var root = document.RootElement;
        var capabilities = root.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array
            ? caps.EnumerateArray().Select(item => item.GetString() ?? "").ToHashSet() : null;
        long? context = null;
        if (root.TryGetProperty("model_info", out var info) && info.ValueKind == JsonValueKind.Object)
            foreach (var property in info.EnumerateObject())
                if (property.Name.EndsWith(".context_length", StringComparison.Ordinal) && property.Value.TryGetInt64(out var length)) context = length;
        return model with
        {
            Vision = capabilities is null ? model.Vision : capabilities.Contains("vision"),
            Tools = capabilities is null ? model.Tools : capabilities.Contains("tools"),
            Reasoning = capabilities is null ? model.Reasoning : capabilities.Contains("thinking"),
            ContextWindow = context ?? model.ContextWindow,
            Capabilities = capabilities?.Order().ToList() ?? model.Capabilities,
        };
    }

    /// <summary>Parses Ollama's /api/tags answer. Size and family come from Ollama; nothing is added.</summary>
    internal static IReadOnlyList<ModelInfo> ParseTags(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return [];
        var list = new List<ModelInfo>();
        foreach (var model in models.EnumerateArray())
        {
            if (!model.TryGetProperty("name", out var nameValue) || nameValue.GetString() is not { Length: > 0 } name) continue;
            var details = model.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;
            string? Detail(string key) => details.ValueKind == JsonValueKind.Object && details.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var parts = new[] { Detail("family"), Detail("parameter_size"), Detail("quantization_level") }.Where(part => part is { Length: > 0 });
            var cloud = IsCloudModel(name);
            list.Add(new ModelInfo
            {
                Id = name, DisplayName = name, Provider = "Ollama", Description = string.Join(" · ", parts),
                Location = cloud ? PrivacyKind.Cloud : PrivacyKind.Local, CostHint = cloud ? null : "Local",
                Availability = cloud ? "Runs on Ollama's servers" : "Downloaded on this computer",
            });
        }
        return list.OrderBy(model => model.Location == PrivacyKind.Cloud).ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(AgentDetection detection, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await GetJsonAsync("api/tags", cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return [];
            return models.EnumerateArray().Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
                .OfType<string>().OrderBy(IsCloudModel).ThenBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { return []; }
    }

    private static async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(new Uri(Endpoint(), path), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentRunResult> RunAsync(AgentDetection detection, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var model = invocation.Model is { Length: > 0 } requested ? requested : _defaultModel();
        if (string.IsNullOrWhiteSpace(model))
            model = (await ListModelsAsync(detection, cancellationToken).ConfigureAwait(false)).FirstOrDefault(name => !IsCloudModel(name));
        if (string.IsNullOrWhiteSpace(model))
            return new AgentRunResult { Outcome = RunOutcome.Unavailable, Reason = "No local Ollama model is available.", FallbackEligible = true };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(invocation.Timeout);
        var command = $"POST {Endpoint()}api/chat (model {model})";
        invocation.OnActivity?.Invoke(new AgentActivity(ActivityKind.Status, $"Ollama is answering with {model}"));
        var text = new StringBuilder();
        UsageReport? usage = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint(), "api/chat"))
            {
                Content = JsonContent.Create(new
                {
                    model,
                    stream = true,
                    messages = new[] { new { role = "user", content = invocation.Prompt, images = await ImagesForAsync(model, invocation, timeout.Token).ConfigureAwait(false) } },
                }),
            };
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                return new AgentRunResult { Outcome = RunOutcome.Failed, Reason = $"Ollama returned {(int)response.StatusCode}: {Shorten(body)}", FallbackEligible = true, CommandLine = command, Duration = DateTimeOffset.UtcNow - started };
            }
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
            {
                if (line.Length == 0) continue;
                using var chunk = JsonDocument.Parse(line);
                var root = chunk.RootElement;
                if (root.TryGetProperty("error", out var error)) return new AgentRunResult { Outcome = RunOutcome.Failed, Reason = "Ollama: " + error.GetString(), FallbackEligible = text.Length == 0, CommandLine = command, Duration = DateTimeOffset.UtcNow - started };
                // "thinking" is the model's internal reasoning and is never used or shown.
                if (root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)) text.Append(content.GetString());
                if (root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
                {
                    usage = new UsageReport
                    {
                        InputTokens = root.TryGetProperty("prompt_eval_count", out var p) && p.TryGetInt64(out var pv) ? pv : null,
                        OutputTokens = root.TryGetProperty("eval_count", out var e) && e.TryGetInt64(out var ev) ? ev : null,
                        CostUsd = IsCloudModel(model) ? null : 0m, Source = "Ollama",
                    };
                }
            }
        }
        catch (OperationCanceledException)
        {
            var cancelled = cancellationToken.IsCancellationRequested;
            return new AgentRunResult { Outcome = cancelled ? RunOutcome.Cancelled : RunOutcome.TimedOut, Reason = cancelled ? "Cancelled by user." : "Ollama did not finish in time.", CommandLine = command, Duration = DateTimeOffset.UtcNow - started };
        }
        catch (HttpRequestException ex)
        {
            return new AgentRunResult { Outcome = RunOutcome.Unavailable, Reason = "Ollama is not reachable: " + ex.Message, FallbackEligible = true, CommandLine = command, Duration = DateTimeOffset.UtcNow - started };
        }
        catch (JsonException ex)
        {
            return new AgentRunResult { Outcome = RunOutcome.Failed, Reason = "Ollama sent an unreadable reply: " + ex.Message, FallbackEligible = text.Length == 0, CommandLine = command, Duration = DateTimeOffset.UtcNow - started };
        }

        var answer = StripThinking(text.ToString()).Trim();
        return answer.Length == 0
            ? new AgentRunResult { Outcome = RunOutcome.NoResult, Reason = "Ollama returned an empty answer.", FallbackEligible = true, CommandLine = command, Usage = usage, Duration = DateTimeOffset.UtcNow - started }
            : new AgentRunResult { Outcome = RunOutcome.Ok, Text = answer, Usage = usage, CommandLine = command, Duration = DateTimeOffset.UtcNow - started };
    }

    /// <summary>Attached images (and video frames), base64-encoded, only for models that report vision. Null otherwise.</summary>
    private static async Task<string[]?> ImagesForAsync(string model, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        var images = invocation.Attachments.SelectMany(Agex.Core.Attachments.AttachmentService.ReadableFiles)
            .Where(path => Agex.Core.Attachments.AttachmentService.Classify(path) == Agex.Core.Attachments.AttachmentKind.Image).Take(8).ToList();
        if (images.Count == 0) return null;
        try
        {
            if (await ShowAsync(model, cancellationToken).ConfigureAwait(false) is not { } show || ApplyShow(new ModelInfo { Id = model }, show).Vision != true) return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { return null; }
        return images.Where(path => new FileInfo(path).Length < 20 * 1024 * 1024).Select(path => Convert.ToBase64String(File.ReadAllBytes(path))).ToArray();
    }

    [GeneratedRegex(@"<think>[\s\S]*?(</think>|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    /// <summary>Removes &lt;think&gt; sections some local models emit; that is internal reasoning, not an answer.</summary>
    public static string StripThinking(string text) => ThinkBlock().Replace(text, "");

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..197] + "...";
}
