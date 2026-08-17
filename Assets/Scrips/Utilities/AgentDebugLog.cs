using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Optional NDJSON debug logger used by temporary instrumentation.</summary>
public static class AgentDebugLog
{
    static readonly object Sync = new object();

    public static string F(float v) => v.ToString("G", CultureInfo.InvariantCulture);
    public static string B(bool v) => v ? "true" : "false";

    public static void Write(string hypothesisId, string location, string message, string dataJsonObject = "{}")
    {
        WriteTo("debug-agent.log", null, hypothesisId, location, message, dataJsonObject);
    }

    public static void WriteSession(string hypothesisId, string location, string message, string dataJsonObject = "{}")
    {
        WriteTo("debug-db624a.log", "db624a", hypothesisId, location, message, dataJsonObject);
    }

    public static void Write914(string hypothesisId, string location, string message, string dataJsonObject = "{}")
    {
        WriteTo("debug-914a21.log", "914a21", hypothesisId, location, message, dataJsonObject);
    }

    static void WriteTo(string fileName, string sessionId, string hypothesisId, string location, string message, string dataJsonObject)
    {
        try
        {
            long ts = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string sessionPart = string.IsNullOrEmpty(sessionId)
                ? ""
                : "\"sessionId\":\"" + sessionId + "\",";
            string line =
                "{" + sessionPart
                + "\"hypothesisId\":\"" + hypothesisId
                + "\",\"location\":\"" + location
                + "\",\"message\":\"" + message
                + "\",\"data\":" + (string.IsNullOrEmpty(dataJsonObject) ? "{}" : dataJsonObject)
                + ",\"timestamp\":" + ts + "}\n";

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", fileName));
            lock (Sync)
                File.AppendAllText(path, line);
        }
        catch
        {
            // ignore IO errors
        }
    }
}
