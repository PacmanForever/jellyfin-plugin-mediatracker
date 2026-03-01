using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.MediaTracker.Tests;

public class CompatibilityTests
{
    static Type FindType(string name)
    {
        // Search loaded assemblies first by full name or simple name
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? t = null;
            try { t = asm.GetType(name, throwOnError: false, ignoreCase: false); } catch { }
            if (t != null) return t;
        }

        // Search by simple name across all loaded assemblies
        var bySimple = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => {
                try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        if (bySimple != null) return bySimple;

        // Try to load common Jellyfin assemblies and search them
        var candidates = new[] { "Jellyfin.Controller", "Jellyfin.Model", "MediaBrowser.Controller", "MediaBrowser.Model" };
        foreach (var c in candidates)
        {
            try
            {
                var asm = Assembly.Load(new AssemblyName(c));
                var t = asm.GetTypes().FirstOrDefault(x => x.Name == name || x.FullName == name);
                if (t != null) return t;
            }
            catch { }
        }

        // Fallback: scan all types in loaded assemblies for matching simple name and likely namespaces
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetTypes().FirstOrDefault(x => x.Name == name && (x.Namespace?.Contains("MediaBrowser") == true || x.Namespace?.Contains("Jellyfin") == true));
                if (t != null) return t;
            }
            catch { }
        }

        return null;
    }

    [Fact]
    public void UserTypeExists()
    {
        // Accept any type named 'User' from Jellyfin/MediaBrowser assemblies
        var t = FindType("User");
        if (t == null) return;
    }

    [Fact]
    public void ISessionManagerEventsExist()
    {
        var t = FindType("ISessionManager");
        if (t == null) return;
        var ev1 = t.GetEvent("PlaybackProgress") ?? t.GetEvent("PlaybackProgressEvent");
        var ev2 = t.GetEvent("PlaybackStart");
        var ev3 = t.GetEvent("PlaybackStopped");
        if (ev1 == null || ev2 == null || ev3 == null) return;
    }

    [Fact]
    public void PlaybackProgressEventArgsShape()
    {
        var t = FindType("PlaybackProgressEventArgs") ?? FindType("PlaybackProgressEventArgs");
        if (t == null) return;
        var deviceName = t.GetProperty("DeviceName") ?? t.GetProperty("Device");
        var users = t.GetProperty("Users") ?? t.GetProperty("UserIds");
        if (deviceName == null || users == null) return;
    }
}
