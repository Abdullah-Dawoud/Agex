// AGEX desktop: application entry point, main window shell and shared state.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Agex.Desktop
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\AGEX-Desktop-Single-Instance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("AGEX is already open.", "AGEX", MessageBoxButton.OK, MessageBoxImage.Information);
                    return 0;
                }
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    // A UI error must never take the engine or the running request down.
                    AppLog.Write("ui_error", e.Exception.ToString());
                    MessageBox.Show("AGEX hit an unexpected display error and recovered.\n\n" + e.Exception.Message, "AGEX", MessageBoxButton.OK, MessageBoxImage.Warning);
                    e.Handled = true;
                };
                var root = EngineClient.FindRoot();
                if (root == null)
                {
                    MessageBox.Show("AGEX engine files were not found next to AGEX.exe. Run 'agex repair' or reinstall AGEX.", "AGEX", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
                var window = new MainWindow(root, args.Contains("--minimized"));
                return app.Run(window);
            }
        }
    }

    public static class AppLog
    {
        public static string Folder
        {
            get
            {
                var home = Environment.GetEnvironmentVariable("AGEX_HOME");
                if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AGEX");
                return home;
            }
        }

        public static void Write(string evt, string detail)
        {
            try
            {
                var dir = Path.Combine(Folder, "logs");
                Directory.CreateDirectory(dir);
                var line = J.Write(new Dictionary<string, object> { { "at", DateTime.UtcNow.ToString("o") }, { "event", evt }, { "detail", detail ?? "" } });
                File.AppendAllText(Path.Combine(dir, "desktop.jsonl"), line + Environment.NewLine);
            }
            catch { }
        }
    }

    public class AppState
    {
        public Dictionary<string, object> State;
        public Dictionary<string, object> Scan;
        public Dictionary<string, object> Settings;
        public Dictionary<string, object> Hello;
        public readonly List<Dictionary<string, object>> Messages = new List<Dictionary<string, object>>();
        public readonly List<Dictionary<string, object>> Events = new List<Dictionary<string, object>>();
        public string RequestId = "";
        public string Status { get { return J.S(State, "status"); } }
        public bool Running { get { return J.B(State, "running"); } }
    }

    public interface IAgexView
    {
        FrameworkElement Root { get; }
        void OnShow();
        void OnUpdate(string kind, Dictionary<string, object> evt);
    }

    public class MainWindow : Window
    {
        public readonly AppState Model = new AppState();
        public EngineClient Engine;
        readonly string root;
        readonly Dictionary<string, IAgexView> views = new Dictionary<string, IAgexView>();
        readonly Dictionary<string, Button> nav = new Dictionary<string, Button>();
        readonly ContentControl content = new ContentControl();
        readonly Border banner = new Border { Visibility = Visibility.Collapsed, Padding = new Thickness(16, 8, 16, 8) };
        readonly TextBlock bannerText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
        readonly StackPanel rightPanel = new StackPanel();
        readonly Border rightHost = new Border { Width = 270, BorderThickness = new Thickness(1, 0, 0, 0) };
        readonly TextBlock engineStatus = new TextBlock { FontSize = 11, Margin = new Thickness(16, 4, 16, 12), TextWrapping = TextWrapping.Wrap };
        string currentView = "Home";
        int restarts;
        bool closing;
        public string DataFolder { get { return AppLog.Folder; } }

        public MainWindow(string root, bool minimized)
        {
            this.root = root;
            Title = "AGEX AI CONTROL CENTER";
            Width = 1280; Height = 820; MinWidth = 860; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (minimized) WindowState = WindowState.Minimized;
            FontFamily = new FontFamily("Segoe UI");
            Theme.Apply(Resources, "Light");
            SetResourceReference(BackgroundProperty, "WindowBrush");
            LoadWindowBounds();
            BuildShell();
            views["Home"] = new HomeView(this);
            views["Collaboration"] = new CollaborationView(this);
            views["Agents"] = new AgentsView(this);
            views["Projects"] = new ProjectsView(this);
            views["Sessions"] = new SessionsView(this);
            views["Settings"] = new SettingsView(this);
            views["Diagnostics"] = new DiagnosticsView(this);
            views["Welcome"] = new WelcomeView(this);
            Navigate("Home");
            SizeChanged += (s, e) => { rightHost.Visibility = e.NewSize.Width < 1100 ? Visibility.Collapsed : Visibility.Visible; };
            Loaded += (s, e) => StartEngine();
            Closing += OnClosing;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.F5) { Engine.Send("scan"); e.Handled = true; } };
        }

        void BuildShell()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sidebar = new DockPanel { LastChildFill = true };
            var sideBorder = new Border { Child = sidebar };
            sideBorder.SetResourceReference(Border.BackgroundProperty, "SidebarBrush");
            var brand = new StackPanel { Margin = new Thickness(20, 22, 16, 22) };
            var title = new TextBlock { Text = "AGEX", FontSize = 26, FontWeight = FontWeights.Bold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "SidebarTextBrush");
            var subtitle = new TextBlock { Text = "AI CONTROL CENTER", FontSize = 11, Opacity = 0.7, Margin = new Thickness(1, 2, 0, 0) };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "SidebarTextBrush");
            brand.Children.Add(title); brand.Children.Add(subtitle);
            DockPanel.SetDock(brand, Dock.Top);
            sidebar.Children.Add(brand);
            engineStatus.SetResourceReference(TextBlock.ForegroundProperty, "SidebarTextBrush");
            engineStatus.Opacity = 0.75;
            DockPanel.SetDock(engineStatus, Dock.Bottom);
            sidebar.Children.Add(engineStatus);
            var navPanel = new StackPanel();
            foreach (var name in new[] { "Home", "Projects", "Agents", "Sessions", "Collaboration", "Settings", "Diagnostics" })
            {
                var label = name == "Collaboration" ? "Agent Room" : name;
                var b = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(20, 10, 12, 10), BorderThickness = new Thickness(3, 0, 0, 0), FontSize = 14, Cursor = Cursors.Hand, Tag = name };
                b.Template = NavTemplate();
                b.SetResourceReference(ForegroundProperty, "SidebarTextBrush");
                b.Background = Brushes.Transparent;
                b.BorderBrush = Brushes.Transparent;
                var target = name;
                b.Click += (s, e) => Navigate(target);
                nav[name] = b;
                navPanel.Children.Add(b);
            }
            sidebar.Children.Add(navPanel);
            grid.Children.Add(sideBorder);

            var main = new DockPanel();
            banner.Background = Theme.Hex("#B7791F");
            banner.Child = bannerText;
            DockPanel.SetDock(banner, Dock.Top);
            main.Children.Add(banner);
            main.Children.Add(content);
            Grid.SetColumn(main, 1);
            grid.Children.Add(main);

            rightHost.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            rightHost.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            rightHost.Child = new ScrollViewer { Content = rightPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 20, 16, 20) };
            Grid.SetColumn(rightHost, 2);
            grid.Children.Add(rightHost);
            Content = grid;
            RenderRightPanel();
        }

        static ControlTemplate navTemplate;
        static ControlTemplate NavTemplate()
        {
            if (navTemplate != null) return navTemplate;
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            border.AppendChild(cp);
            navTemplate = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85));
            navTemplate.Triggers.Add(hover);
            return navTemplate;
        }

        public void Navigate(string name)
        {
            IAgexView view;
            if (!views.TryGetValue(name, out view)) return;
            currentView = name;
            foreach (var pair in nav)
            {
                bool selected = pair.Key == name;
                if (selected) { pair.Value.SetResourceReference(BackgroundProperty, "SidebarSelectedBrush"); pair.Value.SetResourceReference(BorderBrushProperty, "AccentBrush"); pair.Value.FontWeight = FontWeights.SemiBold; }
                else { pair.Value.Background = Brushes.Transparent; pair.Value.BorderBrush = Brushes.Transparent; pair.Value.FontWeight = FontWeights.Normal; }
            }
            content.Content = view.Root;
            try { view.OnShow(); } catch (Exception ex) { AppLog.Write("view_error", ex.ToString()); }
        }

        public T View<T>(string name) where T : class, IAgexView
        {
            IAgexView v;
            return views.TryGetValue(name, out v) ? v as T : null;
        }

        void StartEngine()
        {
            try
            {
                Engine = new EngineClient(root, Dispatcher);
                Engine.EventReceived += OnEngineEvent;
                Engine.EngineStopped += OnEngineStopped;
                Engine.Start();
                engineStatus.Text = "Engine starting...";
            }
            catch (Exception ex)
            {
                ShowBanner("AGEX could not start its engine: " + ex.Message, true);
            }
        }

        void OnEngineStopped(string message)
        {
            if (closing) return;
            AppLog.Write("engine_stopped", message + " " + (Engine != null ? Engine.LastError : ""));
            engineStatus.Text = "Engine stopped";
            if (restarts < 2)
            {
                restarts++;
                ShowBanner(message + " Restarting it...", true);
                Engine.Dispose();
                StartEngine();
            }
            else ShowBanner(message + " Open Diagnostics and choose Restart engine.", true);
        }

        public void RestartEngine()
        {
            restarts = 0;
            if (Engine != null) Engine.Dispose();
            StartEngine();
        }

        void OnEngineEvent(Dictionary<string, object> evt)
        {
            var type = J.S(evt, "type");
            try
            {
                switch (type)
                {
                    case "hello":
                        Model.Hello = evt;
                        engineStatus.Text = "Engine ready  v" + J.S(evt, "version");
                        HideBanner();
                        Engine.Send("settings.get");
                        Engine.Send("scan");
                        Engine.Send("update.check");
                        if (J.B(evt, "firstRun")) Navigate("Welcome");
                        break;
                    case "state":
                        var requestId = J.S(evt, "requestId");
                        if (requestId != Model.RequestId)
                        {
                            Model.RequestId = requestId; Model.Messages.Clear(); Model.Events.Clear();
                            foreach (var v in views.Values) { try { v.OnUpdate("reset", evt); } catch { } }
                        }
                        Model.State = evt;
                        RenderRightPanel();
                        break;
                    case "event":
                        Model.Events.Add(evt);
                        if (Model.Events.Count > 1500) Model.Events.RemoveAt(0);
                        break;
                    case "message":
                        var message = J.O(evt, "message");
                        if (message != null) { Model.Messages.Add(message); if (Model.Messages.Count > 3000) Model.Messages.RemoveAt(0); }
                        break;
                    case "scan": Model.Scan = J.O(evt, "scan"); RenderRightPanel(); break;
                    case "settings":
                        Model.Settings = J.O(evt, "settings");
                        var theme = J.S(Model.Settings, "theme");
                        if (!string.IsNullOrEmpty(theme) && theme != Theme.Current) Theme.Apply(Resources, theme);
                        break;
                    case "update":
                        var update = J.O(evt, "update");
                        if (J.B(update, "Available") && J.B(Model.Settings, "check_updates")) ShowBanner(J.S(update, "Message") + " Open Settings to update.", false);
                        break;
                    case "error":
                        ShowBanner(J.S(evt, "message"), true);
                        break;
                    case "fatal":
                        ShowBanner("Engine error: " + J.S(evt, "message"), true);
                        break;
                }
            }
            catch (Exception ex) { AppLog.Write("event_error", ex.ToString()); }
            foreach (var view in views.Values)
            {
                try { view.OnUpdate(type, evt); } catch (Exception ex) { AppLog.Write("view_update_error", ex.ToString()); }
            }
        }

        public void ShowBanner(string text, bool warning)
        {
            bannerText.Text = text;
            banner.Background = warning ? Theme.Hex("#B45309") : Theme.Hex("#2563EB");
            banner.Visibility = Visibility.Visible;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            timer.Tick += (s, e) => { timer.Stop(); HideBanner(); };
            timer.Start();
        }

        public void HideBanner() { banner.Visibility = Visibility.Collapsed; }

        public void RenderRightPanel()
        {
            rightPanel.Children.Clear();
            rightPanel.Children.Add(U.Section("Agents"));
            var activity = J.O(Model.State, "agentActivity");
            var agents = J.L(Model.State, "agents");
            if (agents.Length == 0 && Model.Scan != null) agents = J.L(Model.Scan, "Agents").Where(a => J.S(a, "Status") == "SUPPORTED").ToArray();
            foreach (var a in agents)
            {
                var name = J.S(a, "name"); if (name == "") name = J.S(a, "Name");
                var act = J.O(activity, name);
                string state;
                if (J.Get(a, "enabled") != null && !J.B(a, "enabled")) state = "Off";
                else if (J.Get(a, "healthy") != null && J.B(a, "checked") && !J.B(a, "healthy")) state = "Unavailable";
                else if (act != null && J.S(act, "state") != "idle") state = char.ToUpper(J.S(act, "state")[0]) + J.S(act, "state").Substring(1);
                else state = "Ready";
                var brush = Theme.StatusBrush(state == "Off" ? "idle" : state);
                var header = new DockPanel();
                var dot = U.Dot(brush);
                header.Children.Add(dot);
                var pill = U.Pill(state, brush);
                DockPanel.SetDock(pill, Dock.Right);
                header.Children.Add(pill);
                header.Children.Add(U.Text(name, 13, true));
                var body = U.Stack(header);
                var current = act != null ? J.S(act, "current") : "";
                if (current != "") { var c = U.Muted(current, 12); c.Margin = new Thickness(17, 4, 0, 0); body.Children.Add(c); }
                else if (state == "Unavailable") { var r = U.Muted(J.S(a, "reason"), 11); r.Margin = new Thickness(17, 4, 0, 0); body.Children.Add(r); }
                if (act != null && J.I(act, "tasks") > 0) { var t = U.Muted(J.I(act, "done") + " of " + J.I(act, "tasks") + " tasks done", 11); t.Margin = new Thickness(17, 2, 0, 0); body.Children.Add(t); }
                var card = U.Card(body, 12);
                rightPanel.Children.Add(card);
            }
            rightPanel.Children.Add(U.Section("Request"));
            var status = Model.Status;
            var info = new StackPanel();
            info.Children.Add(U.Row(U.Dot(Theme.StatusBrush(status == "" ? "idle" : status)), U.Text(Theme.StatusLabel(status), 13, true)));
            if (Model.Running)
            {
                var elapsed = J.I(Model.State, "elapsedSeconds");
                info.Children.Add(U.Muted(string.Format("Elapsed {0}:{1:00}   Workers {2}/2", elapsed / 60, elapsed % 60, J.I(Model.State, "workers"))));
            }
            var tasks = J.L(Model.State, "tasks");
            if (tasks.Length > 0) info.Children.Add(U.Muted(tasks.Count(t => J.S(t, "status") == "DONE") + " of " + tasks.Length + " tasks done"));
            var project = J.S(Model.State, "projectName");
            if (project != "") info.Children.Add(U.Muted("Project: " + project));
            rightPanel.Children.Add(U.Card(info, 12));
        }

        void LoadWindowBounds()
        {
            try
            {
                var file = Path.Combine(DataFolder, "desktop.json");
                if (!File.Exists(file)) return;
                var d = J.Parse(File.ReadAllText(file));
                var w = J.D(d, "width"); var h = J.D(d, "height");
                if (w >= MinWidth && h >= MinHeight && w <= SystemParameters.VirtualScreenWidth && h <= SystemParameters.VirtualScreenHeight) { Width = w; Height = h; }
                if (J.B(d, "maximized") && WindowState != WindowState.Minimized) WindowState = WindowState.Maximized;
            }
            catch { }
        }

        void SaveBounds()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                File.WriteAllText(Path.Combine(DataFolder, "desktop.json"), J.Write(new Dictionary<string, object> { { "width", b.Width }, { "height", b.Height }, { "maximized", WindowState == WindowState.Maximized } }));
            }
            catch { }
        }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (Model.Running && !closing)
            {
                var answer = MessageBox.Show(this, "A request is running. Cancel it and close AGEX?", "AGEX", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) { e.Cancel = true; return; }
            }
            closing = true;
            SaveBounds();
            if (Engine != null) Engine.Dispose();
        }

        public void OpenFolder(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\""); } catch (Exception ex) { ShowBanner(ex.Message, true); }
        }

        public void OpenFile(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                else ShowBanner("File not found: " + path, true);
            }
            catch (Exception ex) { ShowBanner(ex.Message, true); }
        }

        public static string PickFolder(string start)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Choose the project folder AGEX should work in";
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) dialog.SelectedPath = start;
                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        public void CopyText(string text)
        {
            try { Clipboard.SetText(text ?? ""); ShowBanner("Copied.", false); } catch { }
        }
    }
}
