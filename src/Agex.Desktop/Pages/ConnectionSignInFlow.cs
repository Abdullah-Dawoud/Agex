using Agex.Core.Connections;
using Agex.Core.Skills;
using Agex.Desktop.Ui;
using Avalonia.Controls;
using Avalonia.Media;

namespace Agex.Desktop.Pages;

/// <summary>Shared in-context sign-in state for connection cards and skill settings.</summary>
public sealed class ConnectionSignInFlow(MainWindow window)
{
    private Workspace Workspace => window.Workspace;

    public Task<bool> RunSkillAsync(SkillManifest manifest)
    {
        if (manifest.Auth is not { Type: SkillAuthType.CliLogin } auth) return Task.FromResult(false);
        var tool = Workspace.Core.Skills.ToolPath(auth.LoginTool);
        return RunAsync(manifest.Name, tool, auth.LoginArgs, auth.SetupUrl,
            async token => (await Workspace.Core.CliConnections.VerifyAsync(manifest, token)).Ok);
    }

    public Task<bool> RunGitHubAsync()
    {
        var tool = Workspace.Core.Platform.FindExecutable("gh");
        return RunAsync("GitHub CLI", tool, ["auth", "login"], "https://cli.github.com/manual/gh_auth_login",
            async _ => { await Workspace.CheckGitHubCliAsync(); return Workspace.GhSignedIn == true; });
    }

    private async Task<bool> RunAsync(string name, string? tool, IReadOnlyList<string> arguments, string guide, Func<CancellationToken, Task<bool>> verify)
    {
        var status = Kit.Text("Connecting", "body");
        status.TextWrapping = TextWrapping.Wrap;
        var surface = Kit.Text("Sign-in opens in a terminal and may open your system browser. Return here after completing it; AGEX checks automatically.", "small");
        surface.TextWrapping = TextWrapping.Wrap;
        var actions = Kit.Wrap(
            Kit.Button("Open sign-in guide", () =>
            {
                if (Uri.TryCreate(guide, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) Workspace.Core.Platform.OpenUrl(uri);
            }, "subtle", Icons.External));
        var body = Kit.Column(10, status, surface, actions);
        using var cancel = new CancellationTokenSource();
        var dialog = window.Dialogs.ShowAsync("Connect " + name, body, ["Cancel"], cancelIndex: 0);
        var connected = false;
        var watching = WatchAsync();
        await dialog;
        cancel.Cancel();
        await watching;
        Workspace.NotifyConnectionsChanged();
        return connected;

        async Task WatchAsync()
        {
            if (tool is null)
            {
                status.Text = "Unavailable: the sign-in program is not installed. Open the guide for setup instructions.";
                return;
            }
            var folder = Workspace.Project?.Path ?? Workspace.Core.Platform.Paths.DataRoot;
            await ConnectionFlow.RunAsync(
                () => Workspace.Core.Platform.RunInTerminal(tool, arguments, folder), verify,
                state =>
                {
                    switch (state)
                    {
                        case ConnectionFlowState.Connecting: status.Text = "Connecting"; break;
                        case ConnectionFlowState.WaitingForSignIn:
                            status.Text = "Waiting for sign-in";
                            surface.Text = "Sign-in terminal opened. If a browser appears, complete sign-in there. AGEX will return to this connection when verified.";
                            break;
                        case ConnectionFlowState.Connected:
                            connected = true;
                            status.Text = "Connected";
                            window.Activate();
                            window.Toast(name + " connected", "Sign-in verified.", ToastKind.Success);
                            window.Dialogs.Close(0);
                            break;
                        case ConnectionFlowState.Failed:
                            status.Text = "Failed: AGEX could not open or verify sign-in. Open the guide or start the CLI yourself, then try again.";
                            break;
                        case ConnectionFlowState.NeedsAttention:
                            status.Text = "Needs attention: sign-in was not verified. Check the terminal and try again.";
                            break;
                    }
                }, cancel.Token);
        }
    }
}
