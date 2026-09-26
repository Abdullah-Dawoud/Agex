// Test double for agent CLIs. It speaks the same stdin/stdout protocols AGEX
// uses for Codex (exec --json), Antigravity (stream-json), Claude Code
// (stream-json) and Gemini CLI (json). Behaviour is controlled by environment
// variables so tests never need real accounts:
//   FAKE_PLAN_DIR      folder with leader-1.json, leader-2.json ... (leader replies in order)
//   FAKE_<AGENT>_MODE  ok | fail-start | auth | auth-json | slow | huge | echo | no-result | ask-agent
//   FAKE_LOG           file that receives one line per invocation (agent|role|prompt length)
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var utf8 = new UTF8Encoding(false);
Console.OutputEncoding = utf8;
var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
var argsList = args.ToList();
var agent = argsList.Contains("run") && argsList.Contains("--format") ? "opencode"
    : argsList.Contains("exec") ? "codex"
    : argsList.Contains("--input-format") ? "agy"
    : argsList.Contains("-p") ? "claude"
    : argsList.Contains("--approval-mode") ? "gemini"
    : argsList.Contains("--version") ? "version" : "unknown";

if (agent == "version") { stdout.WriteLine("fake-agent 9.9.9"); return 0; }

var mode = Environment.GetEnvironmentVariable($"FAKE_{agent.ToUpperInvariant()}_MODE") ?? "ok";
if (mode == "fail-start") { Console.Error.WriteLine("error: fake agent failed to start"); return 3; }
if (mode == "auth") { Console.Error.WriteLine("Error: Not logged in. Please log in."); return 1; }
if (mode == "auth-json")
{
    // Gemini CLI 0.9 without credentials: a progress line, then a JSON error document on stderr.
    Console.Error.WriteLine("File C:/cache/ripgrep.zip has been cached");
    Console.Error.WriteLine("{\n  \"error\": {\n    \"type\": \"Error\",\n    \"message\": \"Please set an Auth method in your settings.json or specify one of the following environment variables before running: GEMINI_API_KEY\",\n    \"code\": 1\n  }\n}");
    return 1;
}

// Read the prompt (agy: one JSON line; others: all of stdin).
var stdin = new StreamReader(Console.OpenStandardInput(), utf8);
string prompt;
if (agent == "agy")
{
    var line = stdin.ReadLine() ?? "";
    using var doc = JsonDocument.Parse(line);
    prompt = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
}
else prompt = stdin.ReadToEnd();

var isLeader = prompt.Contains("You are the leader", StringComparison.Ordinal);
var isDelivery = prompt.StartsWith("AGEX message delivery", StringComparison.Ordinal);
var isDirect = prompt.StartsWith("AGEX direct reply.", StringComparison.Ordinal);
var role = isLeader ? "leader" : isDelivery ? "delivery" : isDirect ? "direct" : "executor";
// Format: agent|role|prompt length|working folder|arguments|BOM marker (kept last).
if (Environment.GetEnvironmentVariable("FAKE_LOG") is { Length: > 0 } log)
{
    // Agents run in parallel: another fake agent may be writing the log at the same moment.
    var entry = $"{agent}|{role}|{prompt.Length}|{Directory.GetCurrentDirectory()}|{string.Join(' ', args).Replace('|', '/')}|{(prompt.StartsWith('\uFEFF') ? "BOM" : "nobom")}\n";
    for (var attempt = 0; ; attempt++)
    {
        try { File.AppendAllText(log, entry, utf8); break; }
        catch (IOException) when (attempt < 50) { Thread.Sleep(20); }
    }
}
if (Environment.GetEnvironmentVariable("FAKE_PROMPT_DIR") is { Length: > 0 } promptDir)
{
    Directory.CreateDirectory(promptDir);
    File.WriteAllText(Path.Combine(promptDir, $"{DateTime.UtcNow:HHmmssfffffff}-{Guid.NewGuid():N}-{agent}-{role}.txt"), prompt, utf8);
}

if (mode == "slow") Thread.Sleep(TimeSpan.FromSeconds(120));
if (int.TryParse(Environment.GetEnvironmentVariable("FAKE_DELAY_MS"), out var delay)) Thread.Sleep(delay);

string reply;
if (isLeader)
{
    var dir = Environment.GetEnvironmentVariable("FAKE_PLAN_DIR") ?? "";
    var counterFile = Path.Combine(dir, "counter.txt");
    var n = File.Exists(counterFile) ? int.Parse(File.ReadAllText(counterFile)) + 1 : 1;
    File.WriteAllText(counterFile, n.ToString());
    var planFile = Path.Combine(dir, $"leader-{n}.json");
    reply = File.Exists(planFile) ? File.ReadAllText(planFile, utf8) : """{"goal_status":"COMPLETE","reason":"Nothing left.","verification":"Checked by fake leader.","tasks":[]}""";
    if (reply.Contains("{{ECHO}}"))
    {
        var goal = Regex.Match(prompt, @"ORIGINAL USER GOAL:\r?\n(?<goal>.*?)\r?\n", RegexOptions.Singleline).Groups["goal"].Value;
        reply = reply.Replace("{{ECHO}}", JsonEncodedText.Encode(goal).ToString());
    }
}
else if (isDirect)
{
    reply = prompt.Contains("REQUEST TO PLAN:", StringComparison.Ordinal) ? "Goal: plan.\nSteps:\n1. Add login.\n2. Add tests." : "Hello! I am a fake agent.";
}
else if (isDelivery)
{
    reply = "Use PostgreSQL; it is already configured.";
}
else
{
    var owned = Regex.Match(prompt, @"FILES YOU OWN: (?<files>.*)").Groups["files"].Value.Trim();
    var created = new List<string>();
    if (!owned.StartsWith("(none", StringComparison.Ordinal) && owned.Length > 0 && mode != "no-files")
    {
        foreach (var file in owned.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"written by {agent}: مرحبا ✓ café\n", utf8);
            created.Add(file);
        }
    }
    var result = created.Count > 0 ? "Created " + string.Join(", created ", created) + "." : "Checked the project; no changes needed.";
    if (mode == "echo") result = "ECHO:" + Regex.Match(prompt, @"ORIGINAL USER GOAL:\r?\n(?<goal>.*?)\r?\n", RegexOptions.Singleline).Groups["goal"].Value;
    var messages = mode == "ask-agent"
        ? ""","messages":[{"to":"Codex","type":"QUESTION","content":"Which database should I use?"}]"""
        : "";
    reply = mode == "no-result" ? "" : "{\"result\":" + JsonSerializer.Serialize(result) + messages + "}";
}

switch (agent)
{
    case "codex":
    {
        var outIndex = argsList.IndexOf("-o");
        stdout.WriteLine("""{"type":"thread.started","thread_id":"t1"}""");
        stdout.WriteLine("""{"type":"item.started","item":{"id":"1","type":"command_execution","command":"git status"}}""");
        stdout.WriteLine("""{"type":"item.completed","item":{"id":"2","type":"reasoning","text":"SECRET REASONING MUST NOT SHOW"}}""");
        if (mode == "huge") for (var i = 0; i < 200_000; i++) stdout.WriteLine("{\"type\":\"noise\",\"n\":" + i + ",\"pad\":\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"}");
        if (reply.Length > 0)
        {
            stdout.WriteLine("{\"type\":\"item.completed\",\"item\":{\"id\":\"3\",\"type\":\"agent_message\",\"text\":" + JsonSerializer.Serialize(reply) + "}}");
            if (outIndex >= 0) File.WriteAllText(argsList[outIndex + 1], reply, utf8);
        }
        stdout.WriteLine("""{"type":"turn.completed","usage":{"input_tokens":120,"cached_input_tokens":20,"output_tokens":30}}""");
        return reply.Length > 0 ? 0 : 1;
    }
    case "agy":
    {
        stdout.WriteLine("""{"event":"init","conversation_id":"c1","init":{"model":"fake-model"}}""");
        stdout.WriteLine("""{"event":"step_update","step_update":{"step_index":1,"state":"ACTIVE","step_type":"tool","tool_name":"write_to_file","tool_info":{"name":"write_to_file","parameters":{"TargetFile":"x.txt"}}}}""");
        stdout.WriteLine("""{"event":"step_update","step_update":{"step_index":2,"state":"DONE","step_type":"agent_response","usage":{"input_tokens":50,"output_tokens":10}}}""");
        if (reply.Length > 0) stdout.WriteLine("{\"event\":\"result\",\"result\":{\"status\":\"success\",\"response\":" + JsonSerializer.Serialize(reply) + "}}");
        else stdout.WriteLine("""{"event":"error","error":"fake agy error"}""");
        // Like the real CLI: stay alive until stdin closes.
        stdin.ReadToEnd();
        return 0;
    }
    case "claude":
        stdout.WriteLine("""{"type":"system","subtype":"init"}""");
        stdout.WriteLine("""{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"README.md"}}]}}""");
        stdout.WriteLine("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":" + JsonSerializer.Serialize(reply) + ",\"total_cost_usd\":0.0123,\"usage\":{\"input_tokens\":40,\"output_tokens\":12}}");
        return 0;
    case "gemini":
        stdout.WriteLine("{\"response\":" + JsonSerializer.Serialize(reply) + ",\"stats\":{\"models\":{\"gemini-x\":{\"tokens\":{\"prompt\":33,\"candidates\":7}}}}}");
        return 0;
    case "opencode":
        stdout.WriteLine("""{"type":"step_start","part":{"type":"step-start"}}""");
        stdout.WriteLine("""{"type":"tool_use","part":{"type":"tool","tool":"read","state":{"status":"completed","input":{"filePath":"README.md"}}}}""");
        stdout.WriteLine("""{"type":"reasoning","part":{"type":"reasoning","text":"SECRET REASONING MUST NOT SHOW"}}""");
        stdout.WriteLine("""{"type":"step_finish","part":{"type":"step-finish","tokens":{"input":61,"output":9,"reasoning":0,"cache":{"read":4,"write":0}}}}""");
        stdout.WriteLine("{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(reply) + "}}");
        return 0;
    default:
        Console.Error.WriteLine("unknown invocation: " + string.Join(' ', args));
        return 2;
}
