
using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

public class PreviousAction
{
    private const long minimumNumberOfSecondsBetweenProgressUpdate = 30;

    public PreviousAction(string action, float progress)
    {
        this.action = action;
        this.progress = progress;
        this.date = DateTime.UtcNow;
    }

    public string action { get; }
    public float progress { get; }
    public DateTime date { get; }

    public bool IsWithinTimeLimit()
    {
        return DateTime.UtcNow.Subtract(this.date).TotalSeconds < minimumNumberOfSecondsBetweenProgressUpdate;
    }

    public bool ShouldSkip(string action, float progress)
    {
        // Update if action changed
        if (this.action != action)
        {
            return false;
        }

        // Skip if action and progress is the same
        if (this.progress == progress)
        {
            return true;
        }

        return IsWithinTimeLimit();
    }
}

public class PreviousActions
{
    private static readonly TimeSpan progressStateRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan seenStateRetention = TimeSpan.FromHours(12);
    private const int cleanupInterval = 128;
    private readonly object syncRoot = new();
    private int operationsSinceCleanup;

    public PreviousActions()
    {
        progressDictionary = new Dictionary<Tuple<Guid, Guid>, PreviousAction>();
        markedAsSeenHistory = new Dictionary<Tuple<Guid, Guid>, DateTime>();
    }

    public bool ShouldSkipAction(dynamic user, Video video, float progress, string action)
    {
        var id = Tuple.Create((Guid)user.Id, (Guid)video.Id);
        var now = DateTime.UtcNow;

        lock (syncRoot)
        {
            CleanupExpiredEntries(now);

            if (progressDictionary.TryGetValue(id, out PreviousAction? value))
            {
                if (value.ShouldSkip(action, progress))
                {
                    return true;
                }
            }

            progressDictionary[id] = new PreviousAction(action, progress);
            return false;
        }
    }

    public bool CanMarkAsSeen(dynamic user, Video video)
    {
        var id = Tuple.Create((Guid)user.Id, (Guid)video.Id);
        var now = DateTime.UtcNow;

        lock (syncRoot)
        {
            CleanupExpiredEntries(now);

            if (markedAsSeenHistory.TryGetValue(id, out DateTime value)
                && now.Subtract(value) < seenStateRetention)
            {
                return false;
            }

            markedAsSeenHistory[id] = now;
            return true;
        }
    }

    private void CleanupExpiredEntries(DateTime now)
    {
        operationsSinceCleanup++;
        if (operationsSinceCleanup < cleanupInterval)
        {
            return;
        }

        operationsSinceCleanup = 0;

        foreach (var entry in progressDictionary
                     .Where(entry => now.Subtract(entry.Value.date) >= progressStateRetention)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            progressDictionary.Remove(entry);
        }

        foreach (var entry in markedAsSeenHistory
                     .Where(entry => now.Subtract(entry.Value) >= seenStateRetention)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            markedAsSeenHistory.Remove(entry);
        }
    }

    private Dictionary<Tuple<Guid, Guid>, DateTime> markedAsSeenHistory;
    private Dictionary<Tuple<Guid, Guid>, PreviousAction> progressDictionary;
}
