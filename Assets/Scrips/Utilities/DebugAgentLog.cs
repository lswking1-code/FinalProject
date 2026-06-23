using System;
using System.IO;
using UnityEngine;

public static class DebugAgentLog
{
    static string LogPath => Path.Combine(Application.dataPath, "debug-d9584c.log");

    public static void Log(string hypothesisId, string location, string message, string dataJson)
    {
        try
        {
            var line =
                $"{{\"sessionId\":\"d9584c\",\"hypothesisId\":\"{hypothesisId}\",\"location\":\"{Escape(location)}\",\"message\":\"{Escape(message)}\",\"data\":{dataJson},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}\n";
            File.AppendAllText(LogPath, line);
        }
        catch
        {
            // ignore logging failures
        }
    }

    static string Escape(string value) => value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
}
