using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Layout can keep a "Failed to load" placeholder after a domain reload.
// Closing it once is not enough: maximize/restore rechecks it and spam-logs.
public static class FailedEditorWindowRecovery
{
    const string MaximizeLayoutPath = "UserSettings/Layouts/CurrentMaximizeLayout.dwlt";
    const string LayoutsDir = "UserSettings/Layouts";

    [InitializeOnLoadMethod]
    static void ScheduleRecovery()
    {
        EditorApplication.delayCall += RecoverOnce;
    }

    static void RecoverOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += RecoverOnce;
            return;
        }

        if (CloseFailedWindowsInternal(quietIfNone: true) > 0)
        {
            // Maximize layout may still reference the placeholder; clear and retry once.
            ClearMaximizeLayoutBackup();
            EditorApplication.delayCall += () => CloseFailedWindowsInternal(quietIfNone: true);
        }
    }

    [MenuItem("Tools/Diagnostics/Close Failed Editor Windows")]
    public static void CloseFailedWindows()
    {
        ClearMaximizeLayoutBackup();
        CloseFailedWindowsInternal(quietIfNone: false);
    }

    static int CloseFailedWindowsInternal(bool quietIfNone)
    {
        int closed = 0;
        string backup = null;

        foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
        {
            if (!IsFailedPlaceholder(window))
                continue;

            if (backup == null)
                backup = BackupLayouts();

            try
            {
                if (window.maximized)
                    window.maximized = false;
            }
            catch
            {
                // Ignore — placeholder may already be unusable.
            }

            try
            {
                window.Close();
                closed++;
            }
            catch
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(window, true);
                    closed++;
                }
                catch (Exception destroyEx)
                {
                    Debug.LogWarning(
                        "[FailedEditorWindowRecovery] Could not dispose failed window: "
                        + destroyEx.Message);
                }
            }
        }

        if (closed > 0 || !quietIfNone)
        {
            Debug.Log($"[FailedEditorWindowRecovery] Closed {closed} failed window(s)."
                + (backup != null ? $" Layout backup: {backup}" : ""));
        }

        return closed;
    }

    static bool IsFailedPlaceholder(EditorWindow window)
    {
        if (window == null)
            return false;

        try
        {
            Type type = window.GetType();
            if (type == null || type.FullName != "UnityEditor.FallbackEditorWindow")
                return false;
        }
        catch
        {
            return false;
        }

        try
        {
            if (window.hasUnsavedChanges)
                return false;
        }
        catch
        {
            // Broken placeholder — treat as disposable.
        }

        return true;
    }

    static string BackupLayouts()
    {
        string backup = Path.Combine("Library", "FailedWindowLayoutBackups",
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        if (Directory.Exists(LayoutsDir))
        {
            foreach (string file in Directory.GetFiles(LayoutsDir, "*.dwlt"))
                File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), false);
        }
        return backup;
    }

    static void ClearMaximizeLayoutBackup()
    {
        try
        {
            if (File.Exists(MaximizeLayoutPath))
                File.Delete(MaximizeLayoutPath);
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                "[FailedEditorWindowRecovery] Could not clear maximize layout: " + e.Message);
        }
    }
}
