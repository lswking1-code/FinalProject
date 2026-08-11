using System.IO;
using UnityEngine;

/// <summary>Optional NDJSON debug logger used by temporary instrumentation.</summary>
public static class AgentDebugLog
{
    static readonly object Sync = new object();

    public static void Write(string hypothesisId, string location, string message, string dataJsonObject = "{}")
    {
        try
        {
            long ts = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string line =
                "{\"hypothesisId\":\"" + hypothesisId
                + "\",\"location\":\"" + location
                + "\",\"message\":\"" + message
                + "\",\"data\":" + (string.IsNullOrEmpty(dataJsonObject) ? "{}" : dataJsonObject)
                + ",\"timestamp\":" + ts + "}\n";

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "debug-agent.log"));
            lock (Sync)
                File.AppendAllText(path, line);
        }
        catch
        {
            // ignore IO errors
        }
    }
}
