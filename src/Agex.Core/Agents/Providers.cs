using System.Net.Http.Headers;
using System.Text.Json;
using Agex.Core.Platform;
using Agex.Core.Runtime;

namespace Agex.Core.Agents;

public enum CostLabel { Free, FreeTier, Local, Paid, Unknown }

/// <summary>
/// An OpenAI-compatible model endpoint that an agent can be pointed at instead
/// of its own default service (today: Codex, through its documented
/// <c>model_providers</c> configuration with the Responses API).
/// </summary>
public sealed class ProviderProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Base URL ending in /v1 (http only for this computer; https otherwise).</summary>
    public string BaseUrl { get; set; } = "";
    public bool NeedsKey { get; set; }
    /// <summary>True when the endpoint runs on this computer.</summary>
    public bool Local { get; set; }
    public CostLabel Cost { get; set; } = CostLabel.Unknown;
    public string Notes { get; set; } = "";
    public string Homepage { get; set; } = "";
}

/// <summary>The endpoint an agent run should use (secrets resolved at run time, never stored in settings).</summary>
public sealed record ProviderEndpoint(string Id, string Name, string BaseUrl, bool Local, string? ApiKey);

public static class ProviderPresets
{
    /// <summary>
    /// Presets with facts from each project's own documentation (2026-09-24).
    /// Cost labels are conservative: "Free" only for software that runs on this computer.
    /// </summary>
    public static IReadOnlyList<ProviderProfile> All { get; } =
    [
        new() { Id = "ollama-local", Name = "Ollama on this computer", BaseUrl = "http://127.0.0.1:11434/v1", Local = true, Cost = CostLabel.Local, Homepage = "https://ollama.com",
            Notes = "Free and private. Codex needs a model with reasoning (\"thinking\") support, for example qwen3." },
        new() { Id = "lmstudio-local", Name = "LM Studio on this computer", BaseUrl = "http://127.0.0.1:1234/v1", Local = true, Cost = CostLabel.Local, Homepage = "https://lmstudio.ai",
            Notes = "Free and private. Start LM Studio's local server first." },
        new() { Id = "omniroute", Name = "OmniRoute gateway", BaseUrl = "http://localhost:20128/v1", Local = false, Cost = CostLabel.Unknown, Homepage = "https://github.com/diegosouzapw/OmniRoute",
            Notes = "A router that runs on this computer and forwards requests to the providers you connect in OmniRoute (many have free tiers). Your requests and code go to those providers; check them in OmniRoute." },
        new() { Id = "openrouter", Name = "OpenRouter", BaseUrl = "https://openrouter.ai/api/v1", NeedsKey = true, Cost = CostLabel.Paid, Homepage = "https://openrouter.ai",
            Notes = "Cloud router for many model companies; pay per use, some models are offered free with limits. Needs an OpenRouter API key." },
    ];

    public static string CostText(CostLabel cost) => cost switch
    {
        CostLabel.Free => "Free",
        CostLabel.FreeTier => "Free tier",
        CostLabel.Local => "Local (free)",
        CostLabel.Paid => "Paid",
        _ => "Depends on the providers you connect",
    };
}

public sealed class ProviderService(IPlatformService platform, AgexLog? log = null)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static string SecretKey(string providerId) => $"provider:{providerId}:api-key";

    public static bool IsValidBaseUrl(string url, bool local) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || local && uri.Host is "localhost"));

    public ProviderEndpoint? Endpoint(ProviderProfile? profile) => profile is null || !IsValidBaseUrl(profile.BaseUrl, profile.Local || profile.BaseUrl.Contains("localhost", StringComparison.Ordinal))
        ? null
        : new ProviderEndpoint(profile.Id, profile.Name, profile.BaseUrl.TrimEnd('/'), profile.Local, profile.NeedsKey || platform.SecureStore.Get(SecretKey(profile.Id)) is { Length: > 0 } ? platform.SecureStore.Get(SecretKey(profile.Id)) : null);

    /// <summary>Lists models with the OpenAI-compatible GET /models. No model is run.</summary>
    public async Task<ModelDiscovery> ListModelsAsync(ProviderProfile profile, CancellationToken cancellationToken)
    {
        var source = profile.Name + " /models";
        if (Endpoint(profile) is not { } endpoint) return ModelDiscovery.Failed("The provider address is not valid (use https, or http only for this computer).", source);
        if (profile.NeedsKey && string.IsNullOrEmpty(endpoint.ApiKey)) return ModelDiscovery.Failed($"Add your {profile.Name} API key first.", source);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.BaseUrl + "/models");
            if (endpoint.ApiKey is { Length: > 0 } key) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is 401 or 403) return ModelDiscovery.Failed($"{profile.Name} rejected the API key.", source);
            if (!response.IsSuccessStatusCode) return ModelDiscovery.Failed($"{profile.Name} answered {(int)response.StatusCode}.", source);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var models = new List<ModelInfo>();
            if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var item in data.EnumerateArray())
                    if (item.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } modelId && ModelName.IsValid(modelId))
                        models.Add(new ModelInfo
                        {
                            Id = modelId, DisplayName = item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? modelId : modelId,
                            Provider = profile.Name, Location = profile.Local ? PrivacyKind.Local : PrivacyKind.Cloud,
                            CostHint = profile.Local ? "Local" : modelId.EndsWith(":free", StringComparison.OrdinalIgnoreCase) && profile.Id == "openrouter" ? "Free (OpenRouter)" : null,
                            ContextWindow = item.TryGetProperty("context_length", out var context) && context.TryGetInt64(out var length) ? length : null,
                        });
            return ModelDiscovery.From(models.OrderBy(model => model.CostHint is null).ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase).ToList(), source, $"{profile.Name} reported no models.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log?.Error("provider_models_failed", ex, new { provider = profile.Id });
            return ModelDiscovery.Failed($"{profile.Name} is not reachable at {profile.BaseUrl}." + (profile.Local ? " Start it first." : ""), source);
        }
    }
}
