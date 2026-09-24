using System.Reflection;

namespace Agex.Core;

public static class AgexInfo
{
    public const string ProductName = "AGEX";
    public const string DisplayName = "AGEX AI CONTROL CENTER";
    /// <summary>GitHub repository that publishes AGEX releases and the skill catalog.</summary>
    public const string Repository = "Abdullah-Dawoud/Ai-COGY";

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AgexInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        return informational.Split('+')[0];
    }

    /// <summary>Compares dotted versions ("2.0.0" &lt; "2.1.0"); suffixes are ignored. Returns -1, 0 or 1.</summary>
    public static int CompareVersions(string a, string b)
    {
        static int[] Parts(string value) => value.TrimStart('v', 'V').Split('-', '+')[0].Split('.').Select(part => int.TryParse(part, out var number) ? number : 0).ToArray();
        var x = Parts(a);
        var y = Parts(b);
        for (var index = 0; index < Math.Max(x.Length, y.Length); index++)
        {
            var left = index < x.Length ? x[index] : 0;
            var right = index < y.Length ? y[index] : 0;
            if (left != right) return left < right ? -1 : 1;
        }
        return 0;
    }
}
