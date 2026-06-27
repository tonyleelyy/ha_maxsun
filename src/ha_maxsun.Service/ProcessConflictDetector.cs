using System.Diagnostics;

namespace HaMaxsun.Service;

internal static class ProcessConflictDetector
{
    private static readonly string[] ConflictingProcessNames =
    [
        "MaxsunSync2",
        "MaxsunSyncService"
    ];

    public static IReadOnlyList<string> GetConflicts()
    {
        var conflicts = new List<string>();
        foreach (var name in ConflictingProcessNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        conflicts.Add($"{process.ProcessName}({process.Id})");
                    }
                }
            }
            catch
            {
                // Process enumeration is advisory; failure should not stop the bridge.
            }
        }

        return conflicts;
    }
}

