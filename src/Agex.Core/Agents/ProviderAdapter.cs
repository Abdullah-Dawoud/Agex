using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

/// <summary>AGEX-owned OpenAI-compatible model client for configured routers and providers.</summary>
public sealed class ProviderAdapter(Func<IReadOnlyList<ProviderProfile>> profiles, ProviderService providers) : IAgentAdapter
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    public string Id => "agex-models";
    public string Name => "AGEX Models";
    public string Provider => "Configured model endpoint";
    public string Description => "Use models from OmniRoute or another OpenAI-compatible provider. Connected tools run through AGEX.";
    public AdapterStability Stability => AdapterStability.Beta;
    public IReadOnlySet<Capability> Capabilities { get; } = new HashSet<Capability> { Capability.Planning, Capability.CodeReview, Capability.Debugging, Capability.Documents };
    public bool CanWriteFiles => false;
    public IReadOnlySet<OsKind> SupportedPlatforms { get; } = new HashSet<OsKind> { OsKind.Windows, OsKind.MacOS, OsKind.Linux };
    public int MaxConcurrentRuns => 3;
    public ModelSettingsSupport ModelSettings { get; } = new() { SupportsCustomEndpoint = true, SupportsTemperature = true, Source = "OpenAI-compatible /v1/models and /v1/chat/completions." };
    public PrivacyKind PrivacyFor(string? model) => PrivacyKind.Mixed;
    public string DataDestination(string? model) => "The selected AGEX model provider";
    public AgentSetupInfo Setup { get; } = new()
    {
        Method = InstallMethod.Manual, OfficialUrl = "https://github.com/diegosouzapw/OmniRoute",
        ManualCommands = new Dictionary<OsKind, string>
        {
            [OsKind.Windows] = "Add a model provider in AGEX > Agents > Routing & Providers",
            [OsKind.MacOS] = "Add a model provider in AGEX > Agents > Routing & Providers",
            [OsKind.Linux] = "Add a model provider in AGEX > Agents > Routing & Providers",
        }, WhatGetsInstalled = "No separate agent; add a model endpoint in AGEX.",
        SizeHint = "none", AdminNote = "No installation by AGEX.", AccountNote = "Sign in to the selected provider or router separately.",
        LoginArguments = null, LoginInstructions = "Add a provider and its key under Agents > Routing & Providers.",
        LoginDocsUrl = "https://github.com/diegosouzapw/OmniRoute",
    };
    public bool PassiveAuthCheck => true;
    public Task<AuthCheck> CheckAuthAsync(AgentDetection detection, CancellationToken cancellationToken) => Task.FromResult(AuthCheck.Unknown);
    public AgentDetection Detect(IPlatformService platform) => new()
    {
        Id = Id, Name = Name, Path = "AGEX", Status = profiles().Count > 0 ? AgentStatus.Available : AgentStatus.Broken,
        Reason = profiles().Count > 0 ? "" : "Add a model provider or OmniRoute first.",
    };
    public async Task<AgentDetection> CheckHealthAsync(AgentDetection detection, CancellationToken cancellationToken)
    {
        if (profiles().Count == 0) return detection;
        foreach (var profile in profiles())
            if ((await providers.ListModelsAsync(profile, cancellationToken)).Status == ModelDiscoveryStatus.Ok)
                return detection with { Status = AgentStatus.Supported, Reason = "", CheckedAt = DateTimeOffset.UtcNow };
        return detection with { Status = AgentStatus.Broken, Reason = "No configured model provider answered /models.", CheckedAt = DateTimeOffset.UtcNow };
    }
    public Task<ModelDiscovery> GetModelsAsync(AgentDetection detection, CancellationToken cancellationToken) =>
        profiles().FirstOrDefault() is { } first ? providers.ListModelsAsync(first, cancellationToken)
            : Task.FromResult(ModelDiscovery.Unavailable("Add a provider first."));

    public async Task<AgentRunResult> RunAsync(AgentDetection detection, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (invocation.Provider is not { } provider) return new AgentRunResult { Outcome = RunOutcome.Unavailable, Reason = "Choose a model provider for AGEX Models.", FallbackEligible = true };
        var model = invocation.Model is { Length: > 0 } selected ? selected : provider.Id == "omniroute" ? "auto" : null;
        if (model is null) return new AgentRunResult { Outcome = RunOutcome.Unavailable, Reason = $"Choose a model from {provider.Name}.", FallbackEligible = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(invocation.Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, provider.BaseUrl + "/chat/completions")
            {
                Content = JsonContent.Create(new { model, messages = new[] { new { role = "user", content = invocation.Prompt } }, temperature = invocation.Temperature }),
            };
            if (provider.ApiKey is { Length: > 0 } key) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await Http.SendAsync(request, timeout.Token);
            if ((int)response.StatusCode is 401 or 403) return new AgentRunResult { Outcome = RunOutcome.AuthRequired, Reason = $"{provider.Name} rejected its API key.", FallbackEligible = true };
            if (!response.IsSuccessStatusCode) return new AgentRunResult { Outcome = RunOutcome.Failed, Reason = $"{provider.Name} returned HTTP {(int)response.StatusCode}.", FallbackEligible = true };
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = document.RootElement;
            var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            if (text.Length == 0) return new AgentRunResult { Outcome = RunOutcome.NoResult, Reason = $"{provider.Name} returned no text.", FallbackEligible = true };
            UsageReport? usage = null;
            if (root.TryGetProperty("usage", out var counts)) usage = new UsageReport
            {
                InputTokens = counts.TryGetProperty("prompt_tokens", out var input) && input.TryGetInt64(out var a) ? a : null,
                OutputTokens = counts.TryGetProperty("completion_tokens", out var output) && output.TryGetInt64(out var b) ? b : null,
                Source = provider.Name,
            };
            return new AgentRunResult { Outcome = RunOutcome.Ok, Text = text, Usage = usage };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AgentRunResult { Outcome = RunOutcome.TimedOut, Reason = $"{provider.Name} did not answer in time.", FallbackEligible = true };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new AgentRunResult { Outcome = RunOutcome.Failed, Reason = $"{provider.Name} failed: {Redactor.Redact(ex.Message)}", FallbackEligible = true };
        }
    }
}
