using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Wabbajack.Common;
using Wabbajack.Installer;
using Wabbajack.Installer.Preflight;
using Wabbajack.RateLimiter;

namespace Wabbajack.CLI.Verbs;

/// <summary>
///     Logs what the app shows while an install runs: the installer's status and percent, how far a running
///     preflight check is, and download and hashing speed. Each is logged at most once a second.
/// </summary>
public class ProgressLog
{
    private readonly ILogger _logger;
    private readonly IResource[] _resources;
    private readonly Dictionary<string, long> _next = new();
    private readonly Dictionary<string, (long Bytes, long At)> _sampled = new();
    private readonly Dictionary<string, (long Size, long Done)> _archives = new();

    public ProgressLog(ILogger logger, IEnumerable<IResource> resources)
    {
        _logger = logger;
        _resources = resources.ToArray();
    }

    public void Install(StatusUpdate update)
    {
        var finished = update.StatusCategory == Consts.StepFinished;
        if (!finished && !Due("install"))
            return;
        // The step count is fixed at 15 and not every list runs all of them, so Finished would read below 100.
        var percent = finished ? Percent.One : update.OverallProgress;
        _logger.LogInformation("[{Percent}%] {Status}", (int) (percent.Value * 100), update.StatusText);
    }

    public void Archive(ArchiveStatus status)
    {
        if (status.State is not (ArchiveState.Downloading or ArchiveState.Downloaded))
            return;
        lock (_archives)
            _archives[status.Archive.Name] = (status.Archive.Size,
                status.State == ArchiveState.Downloaded ? status.Archive.Size : status.Progress ?? 0);
    }

    public void Check(CheckStatus check)
    {
        if (check.State != PreflightState.Running || check.ProgressTotal == 0 || !Due(check.Id))
            return;

        var parts = new List<string> {$"{(int) (check.Progress.Value * 100)}%"};
        if (check.Id == PreflightCheckIds.AutomatedDownloads)
        {
            long size, done;
            lock (_archives)
                (size, done) = (_archives.Values.Sum(a => a.Size), _archives.Values.Sum(a => a.Done));
            if (size > 0)
                parts.Add($"{done.ToFileSizeString()} of {size.ToFileSizeString()}");
        }

        var rate = check.Id switch
        {
            PreflightCheckIds.AutomatedDownloads => Rate("Downloads"),
            PreflightCheckIds.ArchiveInventory => Rate("File Hashing"),
            _ => 0
        };
        if (rate > 0)
            parts.Add($"{rate.ToFileSizeString()}/s");

        _logger.LogInformation("{Title}: {Text} ({Progress})", check.Title, check.ProgressText ?? check.Message,
            string.Join(", ", parts));
    }

    private bool Due(string key)
    {
        lock (_next)
        {
            var now = Environment.TickCount64;
            if (now < _next.GetValueOrDefault(key))
                return false;
            _next[key] = now + 1000;
            return true;
        }
    }

    private long Rate(string resource)
    {
        var found = _resources.FirstOrDefault(r => r.Name == resource);
        if (found == null)
            return 0;
        lock (_sampled)
        {
            var now = Environment.TickCount64;
            var bytes = found.StatusReport.Transferred;
            var (lastBytes, lastAt) = _sampled.GetValueOrDefault(resource, (bytes, now));
            _sampled[resource] = (bytes, now);
            return now > lastAt ? (bytes - lastBytes) * 1000 / (now - lastAt) : 0;
        }
    }
}
