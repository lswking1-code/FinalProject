using System.IO;
using UnityEngine;

/// <summary>
/// Debug-session NDJSON logger (session f06fd1). Folded via #region at call sites.
/// </summary>
public static class AgentDebugLog
{
    const string SessionId = "f06fd1";
    static readonly object Gate = new object();
    static string cachedPath;

    static string LogPath
    {
        get
        {
            if (cachedPath != null)
                return cachedPath;
            cachedPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "debug-f06fd1.log"));
            return cachedPath;
        }
    }

    public static void Write(string hypothesisId, string location, string message, string dataJsonObject)
    {
        try
        {
            long ts = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string line =
                "{\"sessionId\":\"" + SessionId +
                "\",\"hypothesisId\":\"" + hypothesisId +
                "\",\"location\":\"" + location +
                "\",\"message\":\"" + Escape(message) +
                "\",\"data\":" + (string.IsNullOrEmpty(dataJsonObject) ? "{}" : dataJsonObject) +
                ",\"timestamp\":" + ts +
                ",\"runId\":\"pre-fix\"}";
            lock (Gate)
            {
                File.AppendAllText(LogPath, line + "\n");
            }
        }
        catch
        {
            // ignore IO failures during debug
        }
    }

    static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
