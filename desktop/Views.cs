// AGEX desktop views. Each view only renders engine state and sends commands.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Line = System.Windows.Shapes.Line;
using Polygon = System.Windows.Shapes.Polygon;
using Path = System.IO.Path;

namespace Agex.Desktop
{
    // ------------------------------------------------------------------ Home
    public class HomeView : IAgexView
    {
        readonly MainWindow app;
        readonly Grid root = new Grid();
        public FrameworkElement Root { get { return root; } }
        readonly TextBlock projectName = U.Text("", 16, true);
        readonly TextBlock projectPath = U.Muted("");
        readonly TextBlock routing = U.Muted("");
        readonly TextBox request = U.Input("", 96, true);
        readonly Button send, cancel, retry;
        readonly TextBlock now = U.Text("Waiting for your request.", 13);
        readonly ProgressBar progress = new ProgressBar { Height = 6, Margin = new Thickness(0, 8, 0, 0), Minimum = 0, Maximum = 1 };
        readonly TextBlock progressText = U.Muted("");
        readonly TabControl tabs = new TabControl { Margin = new Thickness(0, 4, 0, 0) };
        readonly StackPanel resultPanel = new StackPanel();
        readonly ListView taskList = new ListView();
        readonly ListBox changeList = new ListBox { MinWidth = 260 };
        readonly RichTextBox diffBox = new RichTextBox { IsReadOnly = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly TextBlock changesNote = U.Muted("");
        readonly ListBox activityList = new ListBox { BorderThickness = new Thickness(0) };
        readonly CheckBox showTechnical = U.Check("Show technical log", false);
        string lastResultKey = "";
        string lastTasksKey = "";
        int shownEvents;

        public HomeView(MainWindow app)
        {
            this.app = app;
            send = U.Button("Send", (s, e) => Send(), true);
            cancel = U.Button("Cancel", (s, e) => app.Engine.Send("cancel"));
            retry = U.Button("Retry", (s, e) => app.Engine.Send("retry"));
            request.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Send(); e.Handled = true; } };
            request.FontSize = 14;
            System.Windows.Automation.AutomationProperties.SetName(request, "Request");

            var layout = new Grid { Margin = new Thickness(24, 20, 24, 16) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var projectRow = new DockPanel();
            var change = U.Button("Change project", (s, e) => app.Navigate("Projects"));
            DockPanel.SetDock(change, Dock.Right);
            projectRow.Children.Add(change);
            projectRow.Children.Add(U.Stack(projectName, projectPath, routing));
            layout.Children.Add(U.Card(projectRow, 14));

            var buttons = U.Row(send, cancel, retry, U.Muted("Ctrl+Enter sends"));
            ((TextBlock)buttons.Children[3]).VerticalAlignment = VerticalAlignment.Center;
            buttons.Margin = new Thickness(0, 10, 0, 0);
            var requestCard = U.Card(U.Stack(U.Section("What should the agents do?"), request, buttons), 14);
            Grid.SetRow(requestCard, 1);
            layout.Children.Add(requestCard);

            var nowCard = U.Card(U.Stack(now, progress, progressText), 14);
            Grid.SetRow(nowCard, 2);
            layout.Children.Add(nowCard);

            tabs.Items.Add(new TabItem { Header = "Result", Content = new ScrollViewer { Content = resultPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) } });
            BuildTasks();
            tabs.Items.Add(new TabItem { Header = "Tasks", Content = taskList });
            tabs.Items.Add(new TabItem { Header = "Changes", Content = BuildChanges() });
            showTechnical.Checked += (s, e) => RebuildActivity();
            showTechnical.Unchecked += (s, e) => RebuildActivity();
            var activityDock = new DockPanel();
            DockPanel.SetDock(showTechnical, Dock.Top);
            activityDock.Children.Add(showTechnical);
            activityDock.Children.Add(activityList);
            tabs.Items.Add(new TabItem { Header = "Activity", Content = activityDock });
            tabs.SelectionChanged += (s, e) => { if (e.Source == tabs && tabs.SelectedIndex == 2) app.Engine.Send("changes"); };
            Grid.SetRow(tabs, 3);
            layout.Children.Add(tabs);
            root.Children.Add(layout);
            RenderResult();
        }

        void BuildTasks()
        {
            var view = new GridView();
            foreach (var col in new[] { new[] { "Task", "id", "90" }, new[] { "Title", "title", "320" }, new[] { "Agent", "agent", "110" }, new[] { "Status", "status", "120" }, new[] { "Note", "error", "360" } })
                view.Columns.Add(new GridViewColumn { Header = col[0], DisplayMemberBinding = new System.Windows.Data.Binding("[" + col[1] + "]"), Width = double.Parse(col[2]) });
            taskList.View = view;
            taskList.BorderThickness = new Thickness(0);
        }

        FrameworkElement BuildChanges()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new DockPanel();
            var bar = U.Row(U.Button("Refresh", (s, e) => app.Engine.Send("changes")), U.Button("Open file", (s, e) => OpenSelected()), U.Button("Open folder", (s, e) => app.OpenFolder(J.S(app.Model.State, "project"))));
            bar.Margin = new Thickness(0, 4, 0, 6);
            DockPanel.SetDock(bar, Dock.Top);
            left.Children.Add(bar);
            DockPanel.SetDock(changesNote, Dock.Bottom);
            left.Children.Add(changesNote);
            left.Children.Add(changeList);
            changeList.SelectionChanged += (s, e) =>
            {
                var item = changeList.SelectedItem as ListBoxItem;
                if (item != null) app.Engine.Send("diff", new Dictionary<string, object> { { "path", (string)item.Tag } });
            };
            grid.Children.Add(left);
            var diffBorder = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Margin = new Thickness(8, 0, 0, 0), Child = diffBox };
            diffBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            diffBox.SetResourceReference(Control.BackgroundProperty, "CodeBrush");
            diffBox.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            Grid.SetColumn(diffBorder, 1);
            grid.Children.Add(diffBorder);
            return grid;
        }

        void OpenSelected()
        {
            var item = changeList.SelectedItem as ListBoxItem;
            if (item == null) return;
            app.OpenFile(System.IO.Path.Combine(J.S(app.Model.State, "project"), (string)item.Tag));
        }

        void Send()
        {
            var text = request.Text.Trim();
            if (text.Length == 0) return;
            app.Engine.Send("start", new Dictionary<string, object> { { "prompt", text } });
            request.Clear();
            tabs.SelectedIndex = 3;
        }

        public void SetRequestText(string text) { request.Text = text; request.Focus(); }

        public void OnShow() { request.Focus(); RenderState(); }

        public void OnUpdate(string kind, Dictionary<string, object> evt)
        {
            if (kind == "state") RenderState();
            else if (kind == "reset") { activityList.Items.Clear(); changeList.Items.Clear(); diffBox.Document = new FlowDocument(); }
            else if (kind == "event") AppendEvent(evt);
            else if (kind == "changes") RenderChanges(evt);
            else if (kind == "diff") RenderDiff(J.S(evt, "text"));
            else if (kind == "ack" && J.S(evt, "cmd") == "start" && J.I(evt, "queued") > 0) app.ShowBanner("Queued. It starts after the current request.", false);
        }

        void RenderState()
        {
            var s = app.Model.State;
            if (s == null) return;
            projectName.Text = J.S(s, "projectName");
            projectPath.Text = J.S(s, "project");
            routing.Text = string.Format("Leader: {0}    Workload preference: Antigravity {1}% / Codex {2}%", J.S(s, "leader"), J.I(s, "antigravityShare"), J.I(s, "codexShare"));
            var running = J.B(s, "running");
            cancel.IsEnabled = running;
            retry.IsEnabled = !running && app.Model.RequestId != "";
            var lastOutcome = J.O(s, "outcome");
            now.Text = running ? "Now: " + J.S(s, "current") : lastOutcome != null ? "Last request: " + J.S(lastOutcome, "headline") + "  Type a new request above." : J.S(s, "current");
            now.Foreground = running ? Theme.StatusBrush("running") : (Brush)app.FindResource("TextBrush");
            var tasks = J.L(s, "tasks");
            int done = tasks.Count(t => J.S(t, "status") == "DONE");
            int failed = tasks.Count(t => J.S(t, "status") == "FAILED" || J.S(t, "status") == "REPAIR REQUIRED");
            progress.Visibility = tasks.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            progress.Maximum = Math.Max(1, tasks.Length);
            progress.Value = done;
            progressText.Text = tasks.Length > 0 ? string.Format("{0} of {1} tasks done{2}", done, tasks.Length, failed > 0 ? ", " + failed + " failed" : "") : (running ? J.S(s, "stage") : "");
            var tasksKey = string.Join("|", tasks.Select(t => J.S(t, "id") + J.S(t, "status") + J.S(t, "agent") + J.S(t, "error")));
            if (tasksKey != lastTasksKey)
            {
                lastTasksKey = tasksKey;
                taskList.ItemsSource = tasks.Select(t => (Dictionary<string, object>)t).ToList();
            }
            RenderResult();
        }

        void RenderResult()
        {
            var s = app.Model.State;
            var outcome = J.O(s, "outcome");
            var running = J.B(s, "running");
            var key = running ? "running" : J.Write(outcome ?? new Dictionary<string, object>());
            if (key == lastResultKey) return;
            lastResultKey = key;
            resultPanel.Children.Clear();
            if (running) { resultPanel.Children.Add(U.Muted("The result appears here when the request finishes. Watch the agents in the Agent Room.")); resultPanel.Children.Add(U.Button("Open Agent Room", (x, y) => app.Navigate("Collaboration"))); return; }
            if (outcome == null) { resultPanel.Children.Add(U.Muted("No result yet. Type a request above and press Send.")); return; }
            var status = J.S(outcome, "status");
            var headline = U.Text(J.S(outcome, "headline"), 17, true);
            headline.Foreground = Theme.StatusBrush(status);
            resultPanel.Children.Add(U.Row(U.Pill(Theme.StatusLabel(status), Theme.StatusBrush(status)), new Border { Width = 10 }, headline));
            var body = new List<string>();
            var reason = J.S(outcome, "reason");
            if (reason != "") body.Add(reason);
            var verification = J.S(outcome, "verification");
            if (verification != "") body.Add("Verification: " + verification);
            foreach (var r in J.L(outcome, "taskResults")) body.Add(Convert.ToString(r));
            if (body.Count > 0) { var box = U.ReadOnlyBox(string.Join(Environment.NewLine + Environment.NewLine, body)); box.Margin = new Thickness(0, 12, 0, 8); box.MaxHeight = 380; resultPanel.Children.Add(box); }
            var what = J.L(outcome, "whatHappened");
            if (what.Length > 0)
            {
                resultPanel.Children.Add(U.Section("What AGEX did"));
                foreach (var w in what) resultPanel.Children.Add(U.Text("- " + Convert.ToString(w)));
            }
            var failure = J.S(outcome, "primaryFailure");
            if (failure != "" && (status == "FAILED" || status == "START_FAILED" || status == "PARTIAL") && !reason.Contains(failure))
            {
                var f = U.Text("Why: " + failure); f.Foreground = Theme.StatusBrush("failed"); f.Margin = new Thickness(0, 6, 0, 0); resultPanel.Children.Add(f);
            }
            var secs = J.D(outcome, "durationSeconds");
            var stats = string.Format("Tasks {0}  |  Done {1}  |  Failed {2}  |  Codex runs {3}  |  Antigravity runs {4}  |  {5}m {6:00}s", J.I(outcome, "tasks"), J.I(outcome, "done"), J.I(outcome, "failed"), J.I(outcome, "codexRuns"), J.I(outcome, "antigravityRuns"), (int)(secs / 60), (int)(secs % 60));
            var st = U.Muted(stats); st.Margin = new Thickness(0, 10, 0, 10);
            resultPanel.Children.Add(st);
            var actions = U.Wrap(U.Button("Copy result", (x, y) => app.CopyText(J.S(app.Model.State, "result"))), U.Button("View collaboration", (x, y) => app.Navigate("Collaboration")), U.Button("View details", (x, y) => app.Navigate("Diagnostics")), U.Button("Changed files", (x, y) => { tabs.SelectedIndex = 2; }));
            if (status == "FAILED" || status == "START_FAILED" || status == "PARTIAL" || status == "CANCELLED") actions.Children.Insert(0, U.Button("Retry", (x, y) => app.Engine.Send("retry"), true));
            resultPanel.Children.Add(actions);
            if (status != "") tabs.SelectedIndex = 0;
        }

        void AppendEvent(Dictionary<string, object> evt)
        {
            if (!showTechnical.IsChecked.GetValueOrDefault() && J.S(evt, "source") != "AGEX" && J.S(evt, "kind") != "SESSION") return;
            activityList.Items.Add(EventRow(evt));
            if (activityList.Items.Count > 800) activityList.Items.RemoveAt(0);
            activityList.ScrollIntoView(activityList.Items[activityList.Items.Count - 1]);
        }

        void RebuildActivity()
        {
            activityList.Items.Clear();
            foreach (var e in app.Model.Events) AppendEvent(e);
        }

        static FrameworkElement EventRow(Dictionary<string, object> evt)
        {
            var kind = J.S(evt, "kind");
            string glyph = "•"; Brush brush = null;
            switch (kind)
            {
                case "PASS": glyph = "✓"; brush = Theme.StatusBrush("done"); break;
                case "FAIL": glyph = "✗"; brush = Theme.StatusBrush("failed"); break;
                case "START": glyph = "→"; brush = Theme.StatusBrush("running"); break;
                case "WARNING": case "FALLBACK": glyph = "⚠"; brush = Theme.StatusBrush("warning"); break;
            }
            var t = U.Text(J.LocalTime(J.S(evt, "at")) + "  " + glyph + "  " + (J.S(evt, "source") != "AGEX" && J.S(evt, "source") != "AGEX-SESSION" ? "[" + J.S(evt, "source") + "] " : "") + J.S(evt, "message"), 12);
            if (brush != null) t.Foreground = brush;
            return t;
        }

        void RenderChanges(Dictionary<string, object> evt)
        {
            changeList.Items.Clear();
            var items = J.L(evt, "items");
            foreach (var i in items)
            {
                var action = J.S(i, "action");
                var label = action == "A" ? "Added" : action == "D" ? "Deleted" : "Modified";
                var stats = (J.S(i, "added") != "" ? "+" + J.S(i, "added") + " " : "") + (J.S(i, "removed") != "" ? "-" + J.S(i, "removed") : "");
                var row = U.Stack(U.Text(J.S(i, "path"), 12, true), U.Muted(label + "  " + stats, 11));
                changeList.Items.Add(new ListBoxItem { Content = row, Tag = J.S(i, "path"), Padding = new Thickness(6, 4, 6, 4) });
            }
            changesNote.Text = items.Length == 0 ? "No changed files." : items.Length + " changed file(s)." + (J.B(evt, "git") ? "" : " Not a Git repository: diffs are not available.");
        }

        void RenderDiff(string text)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(8), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12 };
            doc.PageWidth = 4000;
            var para = new Paragraph { Margin = new Thickness(0) };
            foreach (var line in (text ?? "").Split('\n'))
            {
                var run = new Run(line.TrimEnd('\r') + "\n");
                if (line.StartsWith("+") && !line.StartsWith("+++")) run.Foreground = Theme.StatusBrush("done");
                else if (line.StartsWith("-") && !line.StartsWith("---")) run.Foreground = Theme.StatusBrush("failed");
                else if (line.StartsWith("@@")) run.Foreground = Theme.StatusBrush("running");
                para.Inlines.Add(run);
            }
            doc.Blocks.Add(para);
            diffBox.Document = doc;
        }
    }

    // ----------------------------------------------------- Agent Room (collaboration)
    public class CollaborationView : IAgexView
    {
        readonly MainWindow app;
        readonly DockPanel root = new DockPanel { Margin = new Thickness(24, 20, 24, 16) };
        public FrameworkElement Root { get { return root; } }
        readonly ComboBox agentFilter = U.Combo(new[] { "All agents", "User", "AGEX", "Codex", "Antigravity" }, "All agents");
        readonly ComboBox taskFilter = U.Combo(new[] { "All tasks" }, "All tasks");
        readonly CheckBox follow = U.Check("Follow live", true);
        readonly ToggleButton graphToggle = new ToggleButton { Content = "Graph", Padding = new Thickness(12, 4, 12, 4) };
        readonly ListBox feed = new ListBox { BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        readonly Canvas graph = new Canvas { Height = 300, ClipToBounds = true };
        readonly Border graphHost;
        readonly TextBlock sourceNote = U.Muted("");
        readonly Button backToLive;
        List<Dictionary<string, object>> saved;
        int shown;

        public CollaborationView(MainWindow app)
        {
            this.app = app;
            ScrollViewer.SetHorizontalScrollBarVisibility(feed, ScrollBarVisibility.Disabled);
            feed.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
            backToLive = U.Button("Back to live", (s, e) => { saved = null; Rebuild(); });
            backToLive.Visibility = Visibility.Collapsed;
            var head = U.Header("Agent Room");
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);
            var note = U.Muted("Messages the agents actually exchanged, their results, task handoffs and AGEX coordination events. AGEX never shows hidden model reasoning, and never invents messages an agent did not send.");
            note.Margin = new Thickness(0, -6, 0, 10);
            DockPanel.SetDock(note, Dock.Top);
            root.Children.Add(note);
            var bar = U.Row(agentFilter, taskFilter, follow, new Border { Width = 12 }, graphToggle, new Border { Width = 12 }, backToLive, sourceNote);
            sourceNote.VerticalAlignment = VerticalAlignment.Center;
            follow.Margin = new Thickness(0, 0, 12, 0);
            bar.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);
            graphHost = U.Card(graph, 8);
            graphHost.Visibility = Visibility.Collapsed;
            DockPanel.SetDock(graphHost, Dock.Top);
            root.Children.Add(graphHost);
            root.Children.Add(feed);
            agentFilter.SelectionChanged += (s, e) => Rebuild();
            taskFilter.SelectionChanged += (s, e) => Rebuild();
            graphToggle.Checked += (s, e) => { graphHost.Visibility = Visibility.Visible; DrawGraph(); };
            graphToggle.Unchecked += (s, e) => graphHost.Visibility = Visibility.Collapsed;
            graph.SizeChanged += (s, e) => DrawGraph();
        }

        public void OnShow() { Rebuild(); DrawGraph(); }

        public void ShowSaved(Dictionary<string, object> session)
        {
            saved = J.L(session, "Messages").Select(m => (Dictionary<string, object>)m).ToList();
            sourceNote.Text = "Saved session from " + J.LocalDateTime(J.S(session, "StartedAt")) + " (read-only)";
            backToLive.Visibility = Visibility.Visible;
            app.Navigate("Collaboration");
        }

        List<Dictionary<string, object>> Source { get { return saved ?? app.Model.Messages; } }

        public void OnUpdate(string kind, Dictionary<string, object> evt)
        {
            if (kind == "reset" && saved == null) { taskFilter.SelectedIndex = 0; while (taskFilter.Items.Count > 1) taskFilter.Items.RemoveAt(1); Rebuild(); return; }
            if (kind == "message" && saved == null)
            {
                var m = J.O(evt, "message");
                UpdateTaskFilter();
                if (m != null && Matches(m)) { feed.Items.Add(Card(m)); shown++; if (follow.IsChecked.GetValueOrDefault()) feed.ScrollIntoView(feed.Items[feed.Items.Count - 1]); }
            }
            if (kind == "state")
            {
                if (feed.Items.Count > 0 && app.Model.Messages.Count == 0 && saved == null) Rebuild();
                if (graphHost.Visibility == Visibility.Visible) DrawGraph();
            }
        }

        void UpdateTaskFilter()
        {
            var ids = Source.Select(m => J.S(m, "TaskId")).Where(t => t != "").Distinct().ToList();
            foreach (var id in ids) if (!taskFilter.Items.Contains(id)) taskFilter.Items.Add(id);
        }

        bool Matches(Dictionary<string, object> m)
        {
            var agent = agentFilter.SelectedItem as string;
            if (agent != null && agent != "All agents" && J.S(m, "From") != agent && J.S(m, "To") != agent) return false;
            var task = taskFilter.SelectedItem as string;
            if (task != null && task != "All tasks" && J.S(m, "TaskId") != task) return false;
            return true;
        }

        void Rebuild()
        {
            if (saved == null) { sourceNote.Text = ""; backToLive.Visibility = Visibility.Collapsed; }
            UpdateTaskFilter();
            feed.Items.Clear();
            shown = 0;
            foreach (var m in Source) if (Matches(m)) { feed.Items.Add(Card(m)); shown++; }
            if (feed.Items.Count == 0) feed.Items.Add(U.Muted(saved == null ? "No messages yet. Send a request on the Home page; agent messages appear here live." : "This session has no messages."));
            else if (follow.IsChecked.GetValueOrDefault()) feed.ScrollIntoView(feed.Items[feed.Items.Count - 1]);
            DrawGraph();
        }

        FrameworkElement Card(Dictionary<string, object> m)
        {
            var from = J.S(m, "From"); var to = J.S(m, "To"); var type = J.S(m, "Type"); var text = J.S(m, "Text"); var task = J.S(m, "TaskId");
            var header = new DockPanel();
            var time = U.Muted(J.LocalTime(J.S(m, "Timestamp")), 11);
            DockPanel.SetDock(time, Dock.Right);
            header.Children.Add(time);
            var who = U.Text(from + "  →  " + to, 13, true);
            who.Foreground = AgentBrush(from);
            header.Children.Add(U.Row(who, new Border { Width = 10 }, U.Pill(type.Replace('_', ' ').ToLowerInvariant(), TypeBrush(type)), task != "" ? (UIElement)U.Muted("   " + task, 11) : null));
            var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 6, 0, 0), MaxHeight = 88, TextTrimming = TextTrimming.CharacterEllipsis };
            body.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var more = new Button { Content = "Show more", Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Cursor = Cursors.Hand, FontSize = 11, Margin = new Thickness(0, 4, 12, 0), Foreground = Theme.Hex("#3D7BFD") };
            more.Click += (s, e) => { bool expand = !double.IsInfinity(body.MaxHeight); body.MaxHeight = expand ? double.PositiveInfinity : 88; more.Content = expand ? "Show less" : "Show more"; };
            var copy = new Button { Content = "Copy", Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Cursor = Cursors.Hand, FontSize = 11, Margin = new Thickness(0, 4, 12, 0), Foreground = Theme.Hex("#3D7BFD") };
            copy.Click += (s, e) => app.CopyText(text);
            var open = new Button { Content = "Open", Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Cursor = Cursors.Hand, FontSize = 11, Margin = new Thickness(0, 4, 12, 0), Foreground = Theme.Hex("#3D7BFD") };
            open.Click += (s, e) => OpenMessage(m);
            var links = U.Row(text.Length > 280 || text.Count(c => c == '\n') > 3 ? more : null, copy, open);
            var card = U.Card(U.Stack(header, body, links), 12);
            card.Margin = new Thickness(0, 0, 8, 8);
            card.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) OpenMessage(m); };
            return card;
        }

        void OpenMessage(Dictionary<string, object> m)
        {
            var w = new Window { Title = J.S(m, "From") + " to " + J.S(m, "To") + " - " + J.S(m, "Type"), Width = 720, Height = 520, Owner = app, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            w.Resources.MergedDictionaries.Add(app.Resources);
            w.SetResourceReference(Window.BackgroundProperty, "CardBrush");
            var box = U.ReadOnlyBox(J.S(m, "Text"));
            box.Margin = new Thickness(16);
            box.FontSize = 13;
            w.Content = box;
            w.Show();
        }

        static Brush AgentBrush(string name)
        {
            switch (name) { case "Codex": return Theme.Hex("#0F766E"); case "Antigravity": return Theme.Hex("#7C3AED"); case "User": return Theme.Hex("#2563EB"); default: return Theme.Hex("#475569"); }
        }

        static Brush TypeBrush(string type)
        {
            switch (type) { case "RESULT": case "ANSWER": return Theme.StatusBrush("done"); case "ASSIGNMENT": return Theme.StatusBrush("running"); case "REVISION_REQUEST": case "REVIEW": case "QUESTION": return Theme.StatusBrush("warning"); case "SYSTEM": return Theme.Hex("#64748B"); default: return Theme.Hex("#64748B"); }
        }

        void DrawGraph()
        {
            if (graphHost.Visibility != Visibility.Visible) return;
            graph.Children.Clear();
            double w = Math.Max(420, graph.ActualWidth), h = graph.Height;
            var names = new List<string> { "User", "AGEX" };
            var agents = J.L(app.Model.State, "agents").Where(a => J.B(a, "enabled")).Select(a => J.S(a, "name")).ToList();
            if (agents.Count == 0) agents = new List<string> { "Codex", "Antigravity" };
            names.AddRange(agents);
            var pos = new Dictionary<string, Point>();
            pos["User"] = new Point(w * 0.10, h / 2);
            pos["AGEX"] = new Point(w * 0.38, h / 2);
            for (int i = 0; i < agents.Count; i++) pos[agents[i]] = new Point(w * 0.78, h * (i + 1) / (agents.Count + 1));
            var messages = Source;
            var edges = new Dictionary<string, DateTime>();
            foreach (var m in messages)
            {
                var from = J.S(m, "From"); var to = J.S(m, "To");
                if (!pos.ContainsKey(from) || !pos.ContainsKey(to) || from == to) continue;
                DateTime at; DateTime.TryParse(J.S(m, "Timestamp"), null, System.Globalization.DateTimeStyles.RoundtripKind, out at);
                edges[from + "|" + to] = at;
            }
            // Coordination always flows User -> AGEX -> agents when a request exists.
            if (app.Model.RequestId != "" && !edges.ContainsKey("User|AGEX")) edges["User|AGEX"] = DateTime.MinValue;
            foreach (var edge in edges)
            {
                var parts = edge.Key.Split('|');
                var a = pos[parts[0]]; var b = pos[parts[1]];
                bool recent = saved == null && (DateTime.UtcNow - edge.Value.ToUniversalTime()).TotalSeconds < 20;
                bool reverse = edges.ContainsKey(parts[1] + "|" + parts[0]);
                double offset = reverse ? 7 : 0;
                var dx = b.X - a.X; var dy = b.Y - a.Y; var len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                var nx = -dy / len * offset; var ny = dx / len * offset;
                var start = new Point(a.X + dx / len * 46 + nx, a.Y + dy / len * 22 + ny);
                var end = new Point(b.X - dx / len * 46 + nx, b.Y - dy / len * 22 + ny);
                var brush = recent ? Theme.StatusBrush("running") : Theme.Hex("#94A3B8");
                graph.Children.Add(new Line { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y, Stroke = brush, StrokeThickness = recent ? 2.5 : 1.5 });
                var angle = Math.Atan2(dy, dx);
                var head = new Polygon { Fill = brush, Points = new PointCollection { end, new Point(end.X - 10 * Math.Cos(angle - 0.4), end.Y - 10 * Math.Sin(angle - 0.4)), new Point(end.X - 10 * Math.Cos(angle + 0.4), end.Y - 10 * Math.Sin(angle + 0.4)) } };
                graph.Children.Add(head);
            }
            var activity = J.O(app.Model.State, "agentActivity");
            foreach (var name in names)
            {
                string state = "idle";
                if (name == "AGEX") state = app.Model.Running ? "running" : (app.Model.Status == "" || app.Model.Status == "IDLE" ? "idle" : app.Model.Status.ToLowerInvariant());
                else if (name == "User") state = "idle";
                else { var act = J.O(activity, name); if (act != null) state = J.S(act, "state"); }
                var brush = Theme.StatusBrush(state);
                var label = U.Stack(U.Text(name, 13, true), U.Muted(state == "idle" ? "" : state, 11));
                ((TextBlock)label.Children[0]).HorizontalAlignment = HorizontalAlignment.Center;
                ((TextBlock)label.Children[1]).HorizontalAlignment = HorizontalAlignment.Center;
                var node = new Border { Width = 92, Height = 44, CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(2), BorderBrush = brush, Child = label, Padding = new Thickness(4, 4, 4, 2) };
                node.SetResourceReference(Border.BackgroundProperty, "CardBrush");
                Canvas.SetLeft(node, pos[name].X - 46);
                Canvas.SetTop(node, pos[name].Y - 22);
                graph.Children.Add(node);
            }
        }
    }

    // ------------------------------------------------------------------ Agents
    public class AgentsView : IAgexView
    {
        readonly MainWindow app;
        readonly StackPanel panel = new StackPanel();
        public FrameworkElement Root { get; private set; }
        readonly Dictionary<string, TextBlock> testResults = new Dictionary<string, TextBlock>();
        bool dirty = true;

        public AgentsView(MainWindow app) { this.app = app; Root = U.Scroll(panel); }

        public void OnShow() { Render(); }

        public void OnUpdate(string kind, Dictionary<string, object> evt)
        {
            if (kind == "scan" || kind == "settings") { dirty = true; if (Root.IsVisible) Render(); }
            if (kind == "agentTest")
            {
                TextBlock t;
                if (testResults.TryGetValue(J.S(evt, "id"), out t)) { t.Text = J.B(evt, "healthy") ? "Connection OK. " + J.S(evt, "reason") : "Not ready: " + J.S(evt, "reason"); t.Foreground = Theme.StatusBrush(J.B(evt, "healthy") ? "done" : "failed"); }
            }
        }

        void Render()
        {
            if (!dirty && panel.Children.Count > 0) return;
            dirty = false;
            panel.Children.Clear();
            testResults.Clear();
            var scan = app.Model.Scan;
            var settings = app.Model.Settings;
            var header = new DockPanel();
            var rescan = U.Button("Rescan", (s, e) => { panel.Children.Clear(); panel.Children.Add(U.Header("Agents")); panel.Children.Add(U.Muted("Scanning your system...")); app.Engine.Send("scan"); });
            DockPanel.SetDock(rescan, Dock.Right);
            header.Children.Add(rescan);
            header.Children.Add(U.Header("Agents"));
            panel.Children.Add(header);
            if (scan == null) { panel.Children.Add(U.Muted("Scanning your system...")); return; }
            var summary = J.O(scan, "Summary");
            panel.Children.Add(U.Muted(string.Format("{0} supported  |  {1} ready  |  {2} detected but not integrated", J.I(summary, "Supported"), J.I(summary, "Ready"), J.I(summary, "Detected"))));
            panel.Children.Add(new Border { Height = 12 });
            var enabled = new List<string>(J.L(settings, "enabled_agents").Select(x => Convert.ToString(x)));
            var checks = new Dictionary<string, CheckBox>();
            panel.Children.Add(U.Section("Agents AGEX can use"));
            foreach (var a in J.L(scan, "Agents").Where(a => J.S(a, "Integration") == "Supported"))
            {
                var id = J.S(a, "Id");
                var ready = J.B(a, "Ready");
                var installed = J.S(a, "Status") == "SUPPORTED";
                var check = U.Check("", enabled.Contains(id));
                check.IsEnabled = installed;
                checks[id] = check;
                var top = new DockPanel();
                var test = U.Button("Test connection", (s, e) => { testResults[id].Text = "Testing..."; app.Engine.Send("agents.test", new Dictionary<string, object> { { "id", id } }); });
                test.IsEnabled = installed;
                DockPanel.SetDock(test, Dock.Right);
                top.Children.Add(test);
                var status = !installed ? "Not installed" : ready ? "Ready" : "Not ready";
                top.Children.Add(U.Row(check, U.Text(J.S(a, "Name"), 15, true), new Border { Width = 10 }, U.Pill(status, Theme.StatusBrush(!installed ? "unavailable" : ready ? "ready" : "warning")), new Border { Width = 10 }, U.Muted(J.S(a, "Version"))));
                var detail = U.Stack(top, U.Muted(J.S(a, "Description") + (J.B(a, "CloudService") ? "  |  Runs on the provider's cloud service." : "")));
                var caps = J.L(a, "Capabilities").Select(c => Convert.ToString(c).Replace('_', ' ').ToLowerInvariant());
                detail.Children.Add(U.Muted("Can: " + string.Join(", ", caps) + (J.Get(a, "CanWriteNow") != null && !J.B(a, "CanWriteNow") ? "  (file edits are off: read-only; change under Advanced)" : ""), 11));
                if (J.S(a, "Location") != "") detail.Children.Add(U.Muted(J.S(a, "Location"), 11));
                if (!ready && J.S(a, "Reason") != "") { var r = U.Text(J.S(a, "Reason"), 12); r.Foreground = Theme.StatusBrush("warning"); detail.Children.Add(r); }
                var result = U.Muted("", 12);
                testResults[id] = result;
                detail.Children.Add(result);
                panel.Children.Add(U.Card(detail, 14));
            }

            panel.Children.Add(U.Section("Routing"));
            var leader = U.Combo(new[] { "Auto", "Codex", "Antigravity" }, J.S(settings, "leader"));
            var strategy = U.Combo(new[] { "Balanced", "Coding-heavy", "Research-heavy", "Custom" }, J.S(settings, "strategy"));
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = J.I(settings, "codex_share"), TickFrequency = 10, IsSnapToTickEnabled = true, Width = 240 };
            var sliderText = U.Muted("");
            Action updateSlider = () => { sliderText.Text = string.Format("Codex {0}% / Antigravity {1}%", (int)slider.Value, 100 - (int)slider.Value); };
            slider.ValueChanged += (s, e) => updateSlider();
            updateSlider();
            var custom = U.Row(slider, new Border { Width = 10 }, sliderText);
            custom.Visibility = (strategy.SelectedItem as string) == "Custom" ? Visibility.Visible : Visibility.Collapsed;
            strategy.SelectionChanged += (s, e) => custom.Visibility = (strategy.SelectedItem as string) == "Custom" ? Visibility.Visible : Visibility.Collapsed;
            var routing = U.Stack(
                U.Labeled("Leader", leader),
                U.Labeled("Workload strategy", strategy),
                U.Labeled("", custom),
                U.Muted("The strategy is a preference. AGEX still routes by agent health and what each agent can do (for example, file edits go to an agent that can write files)."));
            var codexModel = U.Input(J.S(settings, "codex_model"));
            var codexEffort = U.Combo(new[] { "", "low", "medium", "high", "xhigh" }, J.S(settings, "codex_effort"));
            var agyModel = U.Input(J.S(settings, "antigravity_model"));
            var agyEffort = U.Combo(new[] { "", "low", "medium", "high" }, J.S(settings, "antigravity_effort"));
            var writes = U.Check("Allow Codex to edit files in the project (otherwise Codex is read-only and edits go to Antigravity)", J.S(settings, "codex_task_sandbox") == "workspace-write");
            var advanced = new Expander { Header = "Advanced: models and permissions", Margin = new Thickness(0, 8, 0, 0), Content = U.Stack(U.Labeled("Codex model", codexModel), U.Labeled("Codex effort", codexEffort), U.Labeled("Antigravity model", agyModel), U.Labeled("Antigravity effort", agyEffort), writes, U.Muted("Leave a model empty to use the agent's own default.")) };
            advanced.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            routing.Children.Add(advanced);
            var save = U.Button("Save agent settings", (s, e) =>
            {
                var values = new Dictionary<string, object>();
                values["enabled_agents"] = checks.Where(c => c.Value.IsChecked.GetValueOrDefault()).Select(c => c.Key).ToArray();
                values["leader"] = leader.SelectedItem as string;
                values["strategy"] = strategy.SelectedItem as string;
                values["codex_share"] = (int)slider.Value;
                values["antigravity_share"] = 100 - (int)slider.Value;
                values["codex_model"] = codexModel.Text.Trim();
                values["codex_effort"] = codexEffort.SelectedItem as string ?? "";
                values["antigravity_model"] = agyModel.Text.Trim();
                values["antigravity_effort"] = agyEffort.SelectedItem as string ?? "";
                values["codex_task_sandbox"] = writes.IsChecked.GetValueOrDefault() ? "workspace-write" : "read-only";
                if (((string[])values["enabled_agents"]).Length == 0) { app.ShowBanner("Turn on at least one agent.", true); return; }
                app.Engine.Send("settings.set", new Dictionary<string, object> { { "settings", values } });
                app.ShowBanner("Agent settings saved. They apply to the next request.", false);
            }, true);
            save.Margin = new Thickness(0, 12, 0, 0);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            routing.Children.Add(save);
            panel.Children.Add(U.Card(routing, 14));

            var detected = J.L(scan, "Agents").Where(a => J.S(a, "Status") == "DETECTED").ToList();
            panel.Children.Add(U.Section("Detected, not integrated (" + detected.Count + ")"));
            var detList = new StackPanel();
            if (detected.Count == 0) detList.Children.Add(U.Muted("None found."));
            foreach (var a in detected) detList.Children.Add(Line(J.S(a, "Name"), J.S(a, "Version"), J.S(a, "Location"), "AGEX has no adapter for this tool yet."));
            panel.Children.Add(U.Card(detList, 14));

            var ides = J.L(scan, "Ides").Where(a => J.S(a, "Status") == "DETECTED").ToList();
            panel.Children.Add(U.Section("Development environments (" + ides.Count + ")"));
            var ideList = new StackPanel();
            ideList.Children.Add(U.Muted("IDEs are not agents. They are listed so you know what AGEX found.", 11));
            foreach (var a in ides) ideList.Children.Add(Line(J.S(a, "Name"), J.S(a, "Version"), J.S(a, "Location"), ""));
            panel.Children.Add(U.Card(ideList, 14));

            panel.Children.Add(U.Section("Integrations"));
            var integ = new StackPanel();
            foreach (var i in J.L(scan, "Integrations")) integ.Children.Add(U.Row(U.Dot(Theme.StatusBrush(J.S(i, "Status") == "AVAILABLE" ? "ready" : "idle")), U.Text(J.S(i, "Name"), 13, true), new Border { Width = 10 }, U.Muted(J.S(i, "Detail"))));
            panel.Children.Add(U.Card(integ, 14));
        }

        static FrameworkElement Line(string name, string version, string location, string note)
        {
            var s = U.Stack(U.Row(U.Text(name, 13, true), new Border { Width = 8 }, U.Muted(version)), U.Muted(location, 11));
            if (note != "") s.Children.Add(U.Muted(note, 11));
            s.Margin = new Thickness(0, 4, 0, 6);
            return s;
        }
    }

    // ---------------------------------------------------------------- Projects
    public class ProjectsView : IAgexView
    {
        readonly MainWindow app;
        readonly StackPanel panel = new StackPanel();
        public FrameworkElement Root { get; private set; }

        public ProjectsView(MainWindow app) { this.app = app; Root = U.Scroll(panel); }

        public void OnShow() { Render(); }
        public void OnUpdate(string kind, Dictionary<string, object> evt) { if ((kind == "settings" || (kind == "ack" && J.S(evt, "cmd") == "project.set")) && Root.IsVisible) Render(); }

        void Render()
        {
            panel.Children.Clear();
            panel.Children.Add(U.Header("Projects"));
            var current = J.S(app.Model.State, "project");
            panel.Children.Add(U.Card(U.Stack(U.Muted("Current project"), U.Text(J.S(app.Model.State, "projectName"), 16, true), U.Muted(current), U.Wrap(U.Button("Open another folder...", (s, e) => Pick(), true), U.Button("Start without a project", (s, e) => Set(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AGEX-Workspace"), true)), U.Button("Show in Explorer", (s, e) => app.OpenFolder(current)))), 14));
            panel.Children.Add(U.Section("Recent projects"));
            var list = new StackPanel();
            var recent = J.L(app.Model.Settings, "recent_projects").Select(x => Convert.ToString(x)).Where(x => x != "").ToList();
            if (recent.Count == 0) list.Children.Add(U.Muted("No recent projects."));
            foreach (var path in recent)
            {
                var exists = Directory.Exists(path);
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                var open = U.Button(exists ? "Open" : "Missing", (s, e) => Set(path, false));
                open.IsEnabled = exists && !string.Equals(path, current, StringComparison.OrdinalIgnoreCase) && !app.Model.Running;
                DockPanel.SetDock(open, Dock.Right);
                row.Children.Add(open);
                row.Children.Add(U.Stack(U.Text(Path.GetFileName(path.TrimEnd('\\')), 13, true), U.Muted(path + (exists ? "" : "  (folder not found)"), 11)));
                list.Children.Add(row);
            }
            panel.Children.Add(U.Card(list, 14));
            if (app.Model.Running) panel.Children.Add(U.Muted("A request is running. Change the project after it finishes."));
        }

        void Pick()
        {
            var path = MainWindow.PickFolder(J.S(app.Model.State, "project"));
            if (path != null) Set(path, false);
        }

        void Set(string path, bool create)
        {
            if (create) Directory.CreateDirectory(path);
            app.Engine.Send("project.set", new Dictionary<string, object> { { "path", path } });
            app.Engine.Send("settings.get");
            app.ShowBanner("Project: " + path, false);
        }
    }

    // ---------------------------------------------------------------- Sessions
    public class SessionsView : IAgexView
    {
        readonly MainWindow app;
        readonly Grid root = new Grid { Margin = new Thickness(24, 20, 24, 16) };
        public FrameworkElement Root { get { return root; } }
        readonly ListBox list = new ListBox { BorderThickness = new Thickness(0) };
        readonly StackPanel detail = new StackPanel();
        Dictionary<string, object> current;

        public SessionsView(MainWindow app)
        {
            this.app = app;
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new DockPanel();
            var refresh = U.Button("Refresh", (s, e) => app.Engine.Send("sessions.list"));
            DockPanel.SetDock(refresh, Dock.Right);
            head.Children.Add(refresh);
            head.Children.Add(U.Header("Sessions"));
            Grid.SetColumnSpan(head, 2);
            root.Children.Add(head);
            list.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
            Grid.SetRow(list, 1);
            root.Children.Add(list);
            var scroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 0, 0, 0) };
            Grid.SetRow(scroll, 1); Grid.SetColumn(scroll, 1);
            root.Children.Add(scroll);
            list.SelectionChanged += (s, e) => { var item = list.SelectedItem as ListBoxItem; if (item != null) app.Engine.Send("session.get", new Dictionary<string, object> { { "id", (string)item.Tag } }); };
            detail.Children.Add(U.Muted("Select a session to see its request, agents, messages, tasks and result."));
        }

        public void OnShow() { app.Engine.Send("sessions.list"); }

        public void OnUpdate(string kind, Dictionary<string, object> evt)
        {
            if (kind == "sessions") RenderList(J.L(evt, "items"));
            else if (kind == "session") { current = J.O(evt, "session"); RenderDetail(); }
            else if (kind == "state" && Root.IsVisible && !app.Model.Running && J.O(app.Model.State, "outcome") != null) { }
        }

        void RenderList(object[] items)
        {
            list.Items.Clear();
            if (items.Length == 0) { list.Items.Add(new ListBoxItem { Content = U.Muted("No sessions yet."), IsEnabled = false }); return; }
            foreach (var i in items)
            {
                var status = J.S(i, "Status");
                var row = U.Stack(U.Row(U.Dot(Theme.StatusBrush(status)), U.Text(J.S(i, "Request"), 13, true)), U.Muted(string.Format("{0}  |  {1}  |  {2}  |  {3} tasks", J.LocalDateTime(J.S(i, "StartedAt")), Theme.StatusLabel(status), J.S(i, "ProjectName"), J.I(i, "Tasks")), 11));
                ((TextBlock)((StackPanel)row.Children[0]).Children[1]).TextTrimming = TextTrimming.CharacterEllipsis;
                ((TextBlock)((StackPanel)row.Children[0]).Children[1]).TextWrapping = TextWrapping.NoWrap;
                list.Items.Add(new ListBoxItem { Content = row, Tag = J.S(i, "SessionId"), Padding = new Thickness(8, 6, 8, 6) });
            }
        }

        void RenderDetail()
        {
            detail.Children.Clear();
            if (current == null) { detail.Children.Add(U.Muted("Session not found.")); return; }
            var outcome = J.O(current, "Outcome");
            var status = J.S(current, "Status");
            detail.Children.Add(U.Pill(Theme.StatusLabel(status), Theme.StatusBrush(status)));
            var headline = U.Text(J.S(outcome, "Headline"), 16, true); headline.Margin = new Thickness(0, 6, 0, 2);
            detail.Children.Add(headline);
            detail.Children.Add(U.Muted(J.LocalDateTime(J.S(current, "StartedAt")) + "  |  Project " + J.S(current, "ProjectName") + "  |  Leader " + J.S(current, "Leader") + "  |  Agents used: " + J.S(current, "AgentsUsed")));
            detail.Children.Add(U.Section("Request"));
            detail.Children.Add(U.Card(U.ReadOnlyBox(J.S(current, "Request")), 10));
            detail.Children.Add(U.Section("Result"));
            detail.Children.Add(U.Card(U.ReadOnlyBox(J.S(current, "Result")), 10));
            var tasks = J.L(current, "Tasks");
            detail.Children.Add(U.Section("Tasks (" + tasks.Length + ")"));
            var tl = new StackPanel();
            foreach (var t in tasks) tl.Children.Add(U.Stack(U.Row(U.Dot(Theme.StatusBrush(J.S(t, "State"))), U.Text(J.S(t, "TaskId") + "  " + J.S(t, "Title"), 13, true), new Border { Width = 8 }, U.Muted(J.S(t, "AssignedAgent") + "  " + J.S(t, "State"))), U.Muted(J.S(t, "Error") != "" ? J.S(t, "Error") : J.S(t, "Result"), 11)));
            if (tasks.Length == 0) tl.Children.Add(U.Muted("The leader answered directly (no separate tasks)."));
            detail.Children.Add(U.Card(tl, 10));
            var changes = J.L(current, "Changes");
            detail.Children.Add(U.Section("Changed files (" + changes.Length + ")"));
            var cl = new StackPanel();
            foreach (var c in changes) cl.Children.Add(U.Text(J.S(c, "Action") + "  " + J.S(c, "Path") + "  " + J.S(c, "Lines"), 12));
            if (changes.Length == 0) cl.Children.Add(U.Muted("No changes recorded."));
            detail.Children.Add(U.Card(cl, 10));
            var session = current;
            detail.Children.Add(U.Wrap(U.Button("Open in Agent Room (" + J.L(current, "Messages").Length + " messages)", (s, e) => app.View<CollaborationView>("Collaboration").ShowSaved(session), true), U.Button("Copy result", (s, e) => app.CopyText(J.S(session, "Result"))), U.Button("Run again", (s, e) => { app.Navigate("Home"); app.View<HomeView>("Home").SetRequestText(J.S(session, "Request")); })));
        }
    }

    // ---------------------------------------------------------------- Settings
    public class SettingsView : IAgexView
    {
        readonly MainWindow app;
        readonly StackPanel panel = new StackPanel();
        public FrameworkElement Root { get; private set; }
        readonly TextBlock updateText = U.Muted("");

        public SettingsView(MainWindow app) { this.app = app; Root = U.Scroll(panel); }
        public void OnShow() { Render(); }
        public void OnUpdate(string kind, Dictionary<string, object> evt)
        {
            if (kind == "update") { var u = J.O(evt, "update"); updateText.Text = J.S(u, "Message"); }
        }

        void Render()
        {
            var s = app.Model.Settings;
            panel.Children.Clear();
            panel.Children.Add(U.Header("Settings"));
            if (s == null) { panel.Children.Add(U.Muted("Loading...")); return; }
            var startWin = U.Check("Start AGEX when I sign in to Windows", J.B(s, "start_with_windows"));
            var minimized = U.Check("Start minimized", J.B(s, "launch_minimized"));
            var updates = U.Check("Check for updates when AGEX starts", J.B(s, "check_updates"));
            panel.Children.Add(U.Section("General"));
            panel.Children.Add(U.Card(U.Stack(startWin, minimized, updates, U.Row(U.Button("Check for updates now", (x, y) => { updateText.Text = "Checking..."; app.Engine.Send("update.check"); }), updateText), U.Muted("Updates come from AGEX releases on GitHub and are checksum-verified before installing. Run 'agex update' in a terminal to install.")), 14));
            panel.Children.Add(U.Section("Agents and routing"));
            panel.Children.Add(U.Card(U.Stack(U.Muted("Choose agents, the leader, the workload strategy and models on the Agents page."), U.Button("Open Agents", (x, y) => app.Navigate("Agents"))), 14));
            panel.Children.Add(U.Section("Projects"));
            panel.Children.Add(U.Card(U.Stack(U.Muted(J.L(s, "recent_projects").Length + " recent project(s)."), U.Row(U.Button("Open Projects", (x, y) => app.Navigate("Projects")), U.Button("Clear recent list", (x, y) => { Save(new Dictionary<string, object> { { "recent_projects", new string[0] } }); }))), 14));
            var theme = U.Combo(new[] { "Light", "Dark" }, J.S(s, "theme"));
            panel.Children.Add(U.Section("Appearance"));
            panel.Children.Add(U.Card(U.Labeled("Theme", theme), 14));
            var codexTimeout = U.Input(J.S(s, "codex_timeout_seconds"));
            var agyTimeout = U.Input(J.S(s, "antigravity_timeout_seconds"));
            var maxSessions = U.Input(J.S(s, "max_sessions"));
            panel.Children.Add(U.Section("Advanced"));
            panel.Children.Add(U.Card(U.Stack(U.Labeled("Codex time limit (seconds)", codexTimeout), U.Labeled("Antigravity time limit (seconds)", agyTimeout), U.Labeled("Sessions to keep", maxSessions), U.Muted("Codex file access is on the Agents page (Advanced).")), 14));
            var save = U.Button("Save settings", (x, y) =>
            {
                int ct, at, ms;
                if (!int.TryParse(codexTimeout.Text, out ct) || ct < 60 || !int.TryParse(agyTimeout.Text, out at) || at < 60 || !int.TryParse(maxSessions.Text, out ms) || ms < 10) { app.ShowBanner("Time limits must be at least 60 seconds and sessions at least 10.", true); return; }
                Save(new Dictionary<string, object> { { "start_with_windows", startWin.IsChecked.GetValueOrDefault() }, { "launch_minimized", minimized.IsChecked.GetValueOrDefault() }, { "check_updates", updates.IsChecked.GetValueOrDefault() }, { "theme", theme.SelectedItem as string }, { "codex_timeout_seconds", ct }, { "antigravity_timeout_seconds", at }, { "max_sessions", ms } });
                Theme.Apply(app.Resources, theme.SelectedItem as string);
            }, true);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(save);
            panel.Children.Add(new Border { Height = 16 });
            panel.Children.Add(U.Section("Privacy"));
            panel.Children.Add(U.Card(U.Stack(U.Muted("AGEX itself is local. It stores settings, session history (requests, agent messages, results) and sanitized logs in " + app.DataFolder + ". AGEX has no server and sends nothing anywhere."), U.Muted("Codex and Antigravity are cloud services: what you ask them is sent to their providers under their terms.")), 14));
        }

        void Save(Dictionary<string, object> values)
        {
            app.Engine.Send("settings.set", new Dictionary<string, object> { { "settings", values } });
            app.ShowBanner("Settings saved.", false);
        }
    }

    // ------------------------------------------------------------- Diagnostics
    public class DiagnosticsView : IAgexView
    {
        readonly MainWindow app;
        readonly StackPanel panel = new StackPanel();
        public FrameworkElement Root { get; private set; }
        readonly StackPanel repairResults = new StackPanel();
        readonly TextBox details = U.ReadOnlyBox("", true, 260);

        public DiagnosticsView(MainWindow app)
        {
            this.app = app;
            Root = U.Scroll(panel);
            panel.Children.Add(U.Header("Diagnostics"));
            var engineInfo = U.Muted("");
            panel.Children.Add(U.Section("Engine"));
            panel.Children.Add(U.Card(U.Stack(engineInfo, U.Wrap(U.Button("Restart engine", (s, e) => app.RestartEngine()), U.Button("Open logs folder", (s, e) => app.OpenFolder(Path.Combine(app.DataFolder, "logs"))), U.Button("Open data folder", (s, e) => app.OpenFolder(app.DataFolder)))), 14));
            engineInfoBlock = engineInfo;
            panel.Children.Add(U.Section("Repair"));
            panel.Children.Add(U.Card(U.Stack(U.Muted("Checks the AGEX installation, the agex command, the Start Menu shortcut and settings, then detects agents again. It never installs or removes Codex, Antigravity or other tools."), U.Button("Run repair", (s, e) => { repairResults.Children.Clear(); repairResults.Children.Add(U.Muted("Running...")); app.Engine.Send("repair"); }, true), repairResults), 14));
            panel.Children.Add(U.Section("What ran (last request)"));
            details.Height = 320;
            panel.Children.Add(U.Card(U.Stack(U.Button("Refresh", (s, e) => app.Engine.Send("details")), details), 10));
        }

        readonly TextBlock engineInfoBlock;

        public void OnShow()
        {
            engineInfoBlock.Text = app.Model.Hello == null ? "Engine starting..." : string.Format("AGEX {0}  |  engine session {1}\nLog: {2}", J.S(app.Model.Hello, "version"), J.S(app.Model.Hello, "sessionId"), J.S(app.Model.Hello, "logPath"));
            app.Engine.Send("details");
        }

        public void OnUpdate(string kind, Dictionary<string, object> evt)
        {
            if (kind == "details") details.Text = string.Join(Environment.NewLine, J.L(evt, "lines").Select(l => Convert.ToString(l)));
            if (kind == "repair")
            {
                repairResults.Children.Clear();
                foreach (var r in J.L(evt, "results"))
                {
                    var status = J.S(r, "Status");
                    var row = U.Row(U.Pill(status, Theme.StatusBrush(status == "OK" || status == "FIXED" ? "ok" : status == "INFO" ? "idle" : status)), new Border { Width = 8 }, U.Text(J.S(r, "Check"), 12, true), new Border { Width = 8 }, U.Muted(J.S(r, "Detail"), 12));
                    row.Margin = new Thickness(0, 3, 0, 3);
                    repairResults.Children.Add(row);
                    if (J.S(r, "Fix") != "") { var f = U.Muted(J.S(r, "Fix"), 11); f.Margin = new Thickness(60, 0, 0, 2); repairResults.Children.Add(f); }
                }
            }
        }
    }

    // ------------------------------------------------------------- First run
    public class WelcomeView : IAgexView
    {
        readonly MainWindow app;
        readonly StackPanel panel = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        public FrameworkElement Root { get; private set; }
        readonly Dictionary<string, CheckBox> picks = new Dictionary<string, CheckBox>();
        ComboBox primary, strategy;
        string project;

        public WelcomeView(MainWindow app) { this.app = app; Root = U.Scroll(panel); }
        public void OnShow() { Render(); }
        public void OnUpdate(string kind, Dictionary<string, object> evt) { if (kind == "scan" && Root.IsVisible) Render(); }

        void Render()
        {
            panel.Children.Clear();
            picks.Clear();
            panel.Children.Add(U.Header("Welcome to AGEX"));
            var scan = app.Model.Scan;
            if (scan == null)
            {
                panel.Children.Add(U.Text("Scanning your system...", 15));
                panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 12, 0, 0), Width = 320, HorizontalAlignment = HorizontalAlignment.Left });
                return;
            }
            var found = new StackPanel();
            found.Children.Add(U.Section("Agents"));
            foreach (var a in J.L(scan, "Agents").Where(a => J.S(a, "Integration") == "Supported" || J.S(a, "Status") == "DETECTED"))
            {
                var ok = J.B(a, "Ready");
                var supported = J.S(a, "Integration") == "Supported";
                var text = J.S(a, "Name") + (supported ? (ok ? "" : J.S(a, "Status") == "SUPPORTED" ? "  (not ready)" : "  (not found)") : "  (detected, not integrated)");
                found.Children.Add(U.Row(U.Text(ok ? "✓" : "—", 13, true), new Border { Width = 8 }, U.Text(text)));
            }
            found.Children.Add(U.Section("Development environments"));
            var ides = J.L(scan, "Ides").Where(i => J.S(i, "Status") == "DETECTED").ToList();
            if (ides.Count == 0) found.Children.Add(U.Muted("None detected."));
            foreach (var i in ides) found.Children.Add(U.Row(U.Text("✓", 13, true), new Border { Width = 8 }, U.Text(J.S(i, "Name"))));
            found.Children.Add(U.Section("Integrations"));
            foreach (var i in J.L(scan, "Integrations")) found.Children.Add(U.Row(U.Text(J.S(i, "Status") == "AVAILABLE" ? "✓" : "—", 13, true), new Border { Width = 8 }, U.Text(J.S(i, "Name"))));
            panel.Children.Add(U.Card(found, 16));

            panel.Children.Add(U.Section("Which agents should AGEX use?"));
            var choose = new StackPanel();
            var ready = J.L(scan, "Agents").Where(a => J.S(a, "Integration") == "Supported" && J.S(a, "Status") == "SUPPORTED").ToList();
            foreach (var a in ready) { var c = U.Check(J.S(a, "Name") + "  -  " + J.S(a, "Description"), J.B(a, "Ready")); picks[J.S(a, "Id")] = c; choose.Children.Add(c); }
            if (ready.Count == 0) choose.Children.Add(U.Text("No supported agent is installed yet. Install Codex CLI or Antigravity, then choose Rescan on the Agents page."));
            primary = U.Combo(new[] { "Auto" }.Concat(ready.Select(a => J.S(a, "Name"))).ToArray(), ready.Count > 0 ? J.S(ready[0], "Name") : "Auto");
            strategy = U.Combo(new[] { "Balanced", "Coding-heavy", "Research-heavy" }, "Balanced");
            choose.Children.Add(U.Labeled("Primary agent (leader)", primary));
            choose.Children.Add(U.Labeled("Strategy", strategy));
            panel.Children.Add(U.Card(choose, 16));

            panel.Children.Add(U.Section("Project folder"));
            var projectText = U.Muted(project ?? "Choose the folder the agents should work in, or start without a project.");
            panel.Children.Add(U.Card(U.Stack(projectText, U.Row(U.Button("Choose folder...", (s, e) => { var p = MainWindow.PickFolder(null); if (p != null) { project = p; projectText.Text = p; } }), U.Button("Start without a project", (s, e) => { project = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AGEX-Workspace"); Directory.CreateDirectory(project); projectText.Text = project; }))), 16));
            var finish = U.Button("Start using AGEX", (s, e) => Finish(), true);
            finish.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(finish);
        }

        void Finish()
        {
            var enabled = picks.Where(p => p.Value.IsChecked.GetValueOrDefault()).Select(p => p.Key).ToArray();
            if (enabled.Length == 0 && picks.Count > 0) { app.ShowBanner("Choose at least one agent.", true); return; }
            var values = new Dictionary<string, object> { { "first_run_complete", true }, { "enabled_agents", enabled }, { "leader", primary.SelectedItem as string }, { "strategy", strategy.SelectedItem as string } };
            app.Engine.Send("settings.set", new Dictionary<string, object> { { "settings", values } });
            if (!string.IsNullOrEmpty(project)) app.Engine.Send("project.set", new Dictionary<string, object> { { "path", project } });
            app.Navigate("Home");
        }
    }
}
