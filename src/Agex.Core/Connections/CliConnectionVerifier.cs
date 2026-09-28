using Agex.Core.Platform;
using Agex.Core.Runtime;
using Agex.Core.Skills;

namespace Agex.Core.Connections;

/// <summary>Checks CLI account state without reading or storing credentials.</summary>
public sealed class CliConnectionVerifier(IPlatformService platform, ProcessRunner runner, SkillManager skills, ConnectionCheckStore checks)
{
    public static string Configuration(SkillManifest manifest) => "cli-auth|" + manifest.Id + "|" + manifest.Version;

    /// <summary>Resolve the actual CLI, including npm commands whose runtime is Node.</summary>
    public (string? Path, IReadOnlyList<string> Arguments) LoginCommand(SkillManifest manifest)
    {
        var auth = manifest.Auth;
        if (auth is null) return (null, []);
        if (auth.LoginTool == "node" && auth.LoginArgs.FirstOrDefault() is "netlify" or "wrangler")
            return (platform.FindExecutable(auth.LoginArgs[0]), auth.LoginArgs.Skip(1).ToArray());
        return (skills.ToolPath(auth.LoginTool), auth.LoginArgs);
    }

    public async Task<ConnectionCheck> VerifyAsync(SkillManifest manifest, CancellationToken cancellationToken)
    {
        var auth = manifest.Auth;
        var command = auth?.LoginTool switch
        {
            "gh" => (Tool: "gh", Args: new[] { "auth", "status" }),
            "vercel" => (Tool: "vercel", Args: new[] { "whoami" }),
            "render" => (Tool: "render", Args: new[] { "whoami" }),
            "node" when auth.LoginArgs.FirstOrDefault() == "netlify" => (Tool: "netlify", Args: new[] { "status" }),
            "node" when auth.LoginArgs.FirstOrDefault() == "wrangler" => (Tool: "wrangler", Args: new[] { "whoami" }),
            _ => (Tool: "", Args: Array.Empty<string>()),
        };
        var path = command.Tool.Length > 0 ? command.Tool is "netlify" or "wrangler" ? platform.FindExecutable(command.Tool) : skills.ToolPath(command.Tool) : null;
        var ok = false;
        var message = path is null ? "Sign-in check unavailable: required CLI is missing." : "Sign-in still needed.";
        if (path is not null)
        {
            var result = await runner.RunAsync(new ProcessRequest
            {
                FileName = path, Arguments = command.Args, WorkingDirectory = platform.Paths.DataRoot,
                Timeout = TimeSpan.FromSeconds(15), Label = "Connection sign-in check", MaxCaptureChars = 2000,
            }, cancellationToken).ConfigureAwait(false);
            var output = result.Stdout + "\n" + result.Stderr;
            ok = result.Succeeded && !new[] { "not logged in", "not authenticated", "not logged into", "please login", "please log in" }
                .Any(phrase => output.Contains(phrase, StringComparison.OrdinalIgnoreCase));
            message = ok ? "Connected. Sign-in verified." : result.Outcome == ProcessOutcome.TimedOut
                ? "Sign-in check timed out. Check the terminal and try again." : "Sign-in still needed or the CLI could not verify it.";
        }
        var check = new ConnectionCheck
        {
            SkillId = manifest.Id, Agent = "agex", Ok = ok, ToolSeen = ok,
            Configuration = Configuration(manifest), Message = message,
        };
        checks.Record(check);
        return check;
    }
}
