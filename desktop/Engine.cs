// AGEX desktop: engine client and JSON helpers.
//
// The desktop app is a presentation layer. All execution (agents, routing,
// fallback, sessions) runs in the AGEX engine (scripts/agex-primary.ps1 -Serve),
// the same engine the terminal mode uses. The engine is a child process that
// speaks one JSON object per line on stdin/stdout.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace Agex.Desktop
{
    public static class J
    {
        static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 };

        public static Dictionary<string, object> Parse(string json)
        {
            return Serializer.DeserializeObject(json) as Dictionary<string, object>;
        }

        public static string Write(object value)
        {
            return Serializer.Serialize(value);
        }

        public static object Get(object obj, string key)
        {
            var d = obj as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        public static string S(object obj, string key)
        {
            var v = Get(obj, key);
            if (v == null) return "";
            if (v is object[]) return string.Join(", ", Array.ConvertAll((object[])v, x => Convert.ToString(x)));
            return Convert.ToString(v);
        }

        public static int I(object obj, string key)
        {
            var v = Get(obj, key);
            if (v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        public static double D(object obj, string key)
        {
            var v = Get(obj, key);
            if (v == null) return 0;
            try { return Convert.ToDouble(v); } catch { return 0; }
        }

        public static bool B(object obj, string key)
        {
            var v = Get(obj, key);
            if (v is bool) return (bool)v;
            return string.Equals(Convert.ToString(v), "true", StringComparison.OrdinalIgnoreCase);
        }

        public static object[] L(object obj, string key)
        {
            var v = Get(obj, key);
            if (v == null) return new object[0];
            var arr = v as object[];
            if (arr != null) return arr;
            var list = v as ArrayList;
            if (list != null) return list.ToArray();
            return new object[] { v };
        }

        public static Dictionary<string, object> O(object obj, string key)
        {
            return Get(obj, key) as Dictionary<string, object>;
        }

        public static string LocalTime(string iso)
        {
            DateTime t;
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out t)) return "";
            return t.ToLocalTime().ToString("HH:mm:ss");
        }

        public static string LocalDateTime(string iso)
        {
            DateTime t;
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out t)) return "";
            return t.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
    }

    public class EngineClient : IDisposable
    {
        readonly string root;
        readonly Dispatcher dispatcher;
        Process process;
        StreamWriter input;
        readonly object writeLock = new object();
        int requestCounter;
        bool disposed;
        public event Action<Dictionary<string, object>> EventReceived;
        public event Action<string> EngineStopped;
        public bool Running { get { return process != null && !process.HasExited; } }
        public string LastError { get; private set; }

        public EngineClient(string root, Dispatcher dispatcher)
        {
            this.root = root;
            this.dispatcher = dispatcher;
        }

        public static string FindRoot()
        {
            // Installed layout: AGEX.exe next to scripts\. Source checkout: desktop build output one level down.
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 4 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "scripts", "agex-primary.ps1"))) return dir;
                dir = Path.GetDirectoryName(dir.TrimEnd('\\'));
            }
            return null;
        }

        public void Start()
        {
            var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            var script = Path.Combine(root, "scripts", "agex-primary.ps1");
            var psi = new ProcessStartInfo(shell, "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Serve");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            psi.WorkingDirectory = root;
            process = new Process();
            process.StartInfo = psi;
            process.EnableRaisingEvents = true;
            process.Exited += OnExited;
            process.Start();
            input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false));
            input.AutoFlush = true;
            var p = process;
            var readThread = new Thread(() => ReadLoop(p));
            readThread.IsBackground = true;
            readThread.Name = "AGEX engine reader";
            readThread.Start();
            var errThread = new Thread(() => { try { LastError = p.StandardError.ReadToEnd(); } catch { } });
            errThread.IsBackground = true;
            errThread.Start();
        }

        void ReadLoop(Process p)
        {
            try
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    Dictionary<string, object> evt = null;
                    try { evt = J.Parse(line); } catch { }
                    if (evt == null) continue;
                    var handler = EventReceived;
                    if (handler != null) dispatcher.BeginInvoke(new Action(() => handler(evt)));
                }
            }
            catch { }
        }

        void OnExited(object sender, EventArgs e)
        {
            if (disposed) return;
            var handler = EngineStopped;
            var code = 0;
            try { code = process.ExitCode; } catch { }
            if (handler != null) dispatcher.BeginInvoke(new Action(() => handler("The AGEX engine stopped (exit code " + code + ").")));
        }

        public string Send(string cmd, Dictionary<string, object> args)
        {
            var payload = args ?? new Dictionary<string, object>();
            payload["cmd"] = cmd;
            var id = Interlocked.Increment(ref requestCounter).ToString();
            payload["reqId"] = id;
            var json = J.Write(payload);
            lock (writeLock)
            {
                try { if (Running) input.WriteLine(json); } catch { }
            }
            return id;
        }

        public string Send(string cmd)
        {
            return Send(cmd, null);
        }

        public void Stop()
        {
            if (!Running) return;
            try { Send("shutdown"); } catch { }
            try { if (!process.WaitForExit(8000)) KillTree(process.Id); } catch { }
        }

        static void KillTree(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe"), "/PID " + pid + " /T /F");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                using (var k = Process.Start(psi)) { k.WaitForExit(5000); }
            }
            catch { }
        }

        public void Dispose()
        {
            disposed = true;
            Stop();
            try { if (process != null) process.Dispose(); } catch { }
        }
    }
}
