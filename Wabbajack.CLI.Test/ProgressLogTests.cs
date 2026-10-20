using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Wabbajack.CLI.Verbs;
using Wabbajack.DTOs;
using Wabbajack.Installer;
using Wabbajack.Installer.Preflight;
using Wabbajack.RateLimiter;
using Xunit;

namespace Wabbajack.CLI.Test;

public class ProgressLogTests
{
    private readonly ListLogger _logger = new();

    private static StatusUpdate Update(string text) =>
        new("Installing", text, new Percent(0.5), new Percent(0.5), 2);

    private static CheckStatus Check(string id, PreflightState state, double progress, long current, long total) =>
        new(id, "Check", state, "Running", null, Array.Empty<PreflightAction>(), new Percent(progress),
            "3 of 4 finished", false, current, total);

    [Fact]
    public void InstallLogsOncePerSecond()
    {
        var log = new ProgressLog(_logger, Array.Empty<IResource>());

        log.Install(Update("[2/4] Installing files (1/10 files)"));
        log.Install(Update("[2/4] Installing files (2/10 files)"));

        Assert.Equal(new[] {"[37%] [2/4] Installing files (1/10 files)"}, _logger.Lines);
    }

    [Fact]
    public void FinishedStepReadsAsComplete()
    {
        var log = new ProgressLog(_logger, Array.Empty<IResource>());

        log.Install(Update("[14/15] Generating ZEdit Merges"));
        log.Install(new StatusUpdate(Consts.StepFinished, "Finished", new Percent(0.93), Percent.Zero, 14));

        Assert.Equal(new[] {"[37%] [14/15] Generating ZEdit Merges", "[100%] Finished"}, _logger.Lines);
    }

    [Fact]
    public void RunningCheckLogsItsProgress()
    {
        var log = new ProgressLog(_logger, Array.Empty<IResource>());

        log.Check(Check(PreflightCheckIds.ArchiveInventory, PreflightState.Running, 0.25, 1, 4));
        log.Check(Check(PreflightCheckIds.ArchiveInventory, PreflightState.Running, 0.5, 2, 4));

        Assert.Equal(new[] {"Check: 3 of 4 finished (25%)"}, _logger.Lines);
    }

    [Fact]
    public void CheckThatIsNotRunningLogsNothing()
    {
        var log = new ProgressLog(_logger, Array.Empty<IResource>());

        log.Check(Check(PreflightCheckIds.ArchiveInventory, PreflightState.Passed, 1, 4, 4));

        Assert.Empty(_logger.Lines);
    }

    [Fact]
    public void DownloadsShowBytesDone()
    {
        var log = new ProgressLog(_logger, Array.Empty<IResource>());
        var done = new Archive {Name = "a.7z", Size = 1 << 20};
        var going = new Archive {Name = "b.7z", Size = 3 << 20};

        log.Archive(new ArchiveStatus(done, ArchiveState.Downloaded, null, null, null));
        log.Archive(new ArchiveStatus(going, ArchiveState.Downloading, null, 1 << 20, null));
        log.Check(Check(PreflightCheckIds.AutomatedDownloads, PreflightState.Running, 0.5, 1, 2));

        Assert.Equal(new[] {"Check: 3 of 4 finished (50%, 2MB of 4MB)"}, _logger.Lines);
    }

    private class ListLogger : ILogger
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
