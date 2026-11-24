using System;
using System.IO;
// Only invoked by isolated UI verification. No network, user data or other processes.
static class LaunchProbe
{
    static int Main()
    {
        string current = Directory.GetCurrentDirectory();
        string allowed = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","checks")) + Path.DirectorySeparatorChar;
        if (!current.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)) return 2;
        File.WriteAllText(Path.Combine(current,"launch-proof.txt"),current); return 0;
    }
}
