using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.MediaTracker.Tests;

public class CompatibilityTests
{
    static Type FindType(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(name, throwOnError: false, ignoreCase: false);
            if (t != null) return t;
        }
        // Try loading by simple name from referenced assemblies
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.FullName == name || t.Name == name);
    }

    [Fact]
    public void UserTypeExists()
    {
        var t = FindType("MediaBrowser.Controller.Entities.User");
        Assert.NotNull(t);
    }

    [Fact]
    public void ISessionManagerEventsExist()
    {
        var t = FindType("MediaBrowser.Controller.Session.ISessionManager");
        Assert.NotNull(t);
        var ev1 = t.GetEvent("PlaybackProgress");
        var ev2 = t.GetEvent("PlaybackStart");
        var ev3 = t.GetEvent("PlaybackStopped");
        Assert.NotNull(ev1);
        Assert.NotNull(ev2);
        Assert.NotNull(ev3);
    }

    [Fact]
    public void PlaybackProgressEventArgsShape()
    {
        var t = FindType("MediaBrowser.Controller.Session.PlaybackProgressEventArgs") ?? FindType("MediaBrowser.Controller.Session.PlaybackProgressEventArgs");
        Assert.NotNull(t);
        var deviceName = t.GetProperty("DeviceName");
        var users = t.GetProperty("Users");
        Assert.NotNull(deviceName);
        Assert.NotNull(users);
    }
}
