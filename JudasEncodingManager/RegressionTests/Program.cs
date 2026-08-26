using System.Collections.Generic;
using System.IO;
using JudasEncodingManager.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("recognizes all paused and stopped WebUI state names", RecognizesStoppedStateNamesAsync),
    ("does not hand off after an unconfirmed stop response", RejectsUnconfirmedStopAsync),
    ("does not send stop when WebUI already reports paused", AcceptsAlreadyPausedTorrentAsync),
    ("does not move media until exclusive access succeeds", PreventsMoveBeforeExclusiveAccessAsync),
    ("revalidates access when it changes before the move", RevalidatesAccessBeforeMoveAsync),
    ("probes access before every production move retry", ProbesBeforeEveryProductionMoveRetryAsync),
    ("continues with the next queue item after a failed stop", ContinuesQueueAfterFailedStopAsync)
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.Error.WriteLine($"FAIL  {name}");
        Console.Error.WriteLine(ex);
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"{failures.Count} hand-off regression test(s) failed:");
    foreach (var failure in failures)
        Console.Error.WriteLine($" - {failure}");
    return 1;
}

Console.WriteLine();
Console.WriteLine($"{tests.Length} qBittorrent hand-off regression tests passed.");
return 0;

static Task RecognizesStoppedStateNamesAsync()
{
    foreach (var state in new[] { "pausedDL", "pausedUP", "stoppedDL", "stoppedUP", "stopped" })
        Assert(QBittorrentStatePolicy.IsStoppedState(state), $"Expected {state} to be a stopped state.");

    foreach (var state in new[] { null, "", "paused", "downloading", "queuedUP", "stalledUP", "error" })
        Assert(!QBittorrentStatePolicy.IsStoppedState(state), $"Expected {state ?? "<null>"} not to be a stopped state.");

    return Task.CompletedTask;
}

static async Task RejectsUnconfirmedStopAsync()
{
    var qbit = new SimulatedQBittorrentClient(
        states: new[] { "downloading", "downloading" },
        stopResult: false);
    var files = new RecordingFileHandoff();
    var handoff = CreateFastHandoff(qbit, files, maxStopAttempts: 1, maxStateChecks: 1);

    await AssertThrowsAsync<IOException>(() => handoff.StopAndWaitAsync("episode-hash"));
    Assert(qbit.StopCalls == 1, "An active torrent should receive exactly one stop request.");
    Assert(files.MoveCalls == 0, "An unconfirmed stop must not advance to the file move.");
}

static async Task AcceptsAlreadyPausedTorrentAsync()
{
    var qbit = new SimulatedQBittorrentClient(
        states: new[] { "pausedUP", "pausedUP" },
        stopResult: false);
    var handoff = CreateFastHandoff(qbit, new RecordingFileHandoff(), maxStopAttempts: 1, maxStateChecks: 1);

    await handoff.StopAndWaitAsync("episode-hash");

    Assert(qbit.StopCalls == 0, "An already paused torrent should not receive another stop request.");
}

static async Task PreventsMoveBeforeExclusiveAccessAsync()
{
    var qbit = new SimulatedQBittorrentClient(
        states: new[] { "stoppedDL", "stoppedDL" },
        stopResult: true);
    var files = new RecordingFileHandoff();
    var handoff = CreateFastHandoff(qbit, files, maxStopAttempts: 1, maxStateChecks: 1);

    await handoff.StopAndWaitAsync("episode-hash");
    await handoff.MoveAfterExclusiveAccessAsync("downloaded.mkv", "encoding/downloaded.mkv");

    Assert(files.Events.SequenceEqual(new[] { "exclusive-access", "move" }),
        "The hand-off must obtain exclusive access before moving media.");

    var lockedFiles = new RecordingFileHandoff { ExclusiveAccessException = new IOException("file is locked") };
    var lockedHandoff = CreateFastHandoff(qbit, lockedFiles, maxStopAttempts: 1, maxStateChecks: 1);
    await AssertThrowsAsync<IOException>(() => lockedHandoff.MoveAfterExclusiveAccessAsync(
        "downloaded.mkv",
        "encoding/downloaded.mkv"));
    Assert(lockedFiles.MoveCalls == 0, "Media moved despite unavailable exclusive access.");
}

static async Task ContinuesQueueAfterFailedStopAsync()
{
    var failed = new QueueWorkItem("failed stop");
    var next = new QueueWorkItem("next pending");
    var processed = new List<string>();

    await QueueContinuation.ProcessPendingAsync(
        new[] { failed, next },
        item => item.Status == WorkStatus.Pending,
        (item, _) =>
        {
            processed.Add(item.Name);
            if (item == failed)
                throw new IOException("qBittorrent did not confirm stop");

            item.Status = WorkStatus.Completed;
            return Task.CompletedTask;
        },
        (item, _) => item.Status = WorkStatus.Failed);

    Assert(failed.Status == WorkStatus.Failed, "The failed stop should fail only its own queue item.");
    Assert(next.Status == WorkStatus.Completed, "The next pending item did not run after the failed stop.");
    Assert(processed.SequenceEqual(new[] { "failed stop", "next pending" }),
        "Queue processing did not continue in order after the failed stop.");
}

static async Task RevalidatesAccessBeforeMoveAsync()
{
    var qbit = new SimulatedQBittorrentClient(
        states: new[] { "stoppedUP", "stoppedUP" },
        stopResult: true);
    var files = new RecordingFileHandoff { AccessRevokedBeforeMove = true };
    var handoff = CreateFastHandoff(qbit, files, maxStopAttempts: 1, maxStateChecks: 1);

    await AssertThrowsAsync<IOException>(() => handoff.MoveAfterExclusiveAccessAsync(
        "downloaded.mkv",
        "encoding/downloaded.mkv"));

    Assert(files.Events.SequenceEqual(new[] { "exclusive-access", "revalidate-access" }),
        "The source file should be revalidated immediately before the move.");
    Assert(files.MoveCalls == 0, "Media moved after access was revoked.");
}

static async Task ProbesBeforeEveryProductionMoveRetryAsync()
{
    var fileOperations = new RecordingFileMoveOperations { MoveFailuresRemaining = 1 };
    var handoff = new LocalFileHandoff(
        fileOperations,
        (_, _) => Task.CompletedTask);

    await handoff.MoveAsync("downloaded.mkv", "encoding/downloaded.mkv", CancellationToken.None);

    Assert(fileOperations.Events.SequenceEqual(new[]
    {
        "probe", "move",
        "probe", "move"
    }), "LocalFileHandoff must probe exclusive access before every move retry.");
    Assert(fileOperations.MoveCalls == 2, "Expected one failed move attempt followed by a retry.");
}

static DownloadHandoffService CreateFastHandoff(
    IQBittorrentStopClient qbit,
    IFileHandoff files,
    int maxStopAttempts,
    int maxStateChecks)
{
    return new DownloadHandoffService(
        qbit,
        files,
        (_, _) => Task.CompletedTask,
        maxStopAttempts,
        maxStateChecks,
        TimeSpan.Zero,
        TimeSpan.Zero);
}

static async Task AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class SimulatedQBittorrentClient : IQBittorrentStopClient
{
    private readonly Queue<QBittorrentTorrentState?> _states;
    private readonly bool _stopResult;

    public SimulatedQBittorrentClient(IEnumerable<string?> states, bool stopResult)
    {
        _states = new Queue<QBittorrentTorrentState?>(
            states.Select(state => state is null ? null : new QBittorrentTorrentState(state)));
        _stopResult = stopResult;
    }

    public int StopCalls { get; private set; }

    public Task<QBittorrentTorrentState?> GetTorrentStateAsync(
        string hash,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_states.Count > 0 ? _states.Dequeue() : null);
    }

    public Task<bool> StopTorrentAsync(string hash, CancellationToken cancellationToken)
    {
        StopCalls++;
        return Task.FromResult(_stopResult);
    }
}

sealed class RecordingFileHandoff : IFileHandoff
{
    public List<string> Events { get; } = new();
    public int MoveCalls { get; private set; }
    public Exception? ExclusiveAccessException { get; init; }
    public bool AccessRevokedBeforeMove { get; init; }

    public Task WaitForExclusiveAccessAsync(string filePath, CancellationToken cancellationToken)
    {
        Events.Add("exclusive-access");
        if (ExclusiveAccessException is not null)
            return Task.FromException(ExclusiveAccessException);

        return Task.CompletedTask;
    }

    public Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        if (AccessRevokedBeforeMove)
        {
            Events.Add("revalidate-access");
            return Task.FromException(new IOException("file was relocked before move"));
        }

        MoveCalls++;
        Events.Add("move");
        return Task.CompletedTask;
    }
}

sealed class RecordingFileMoveOperations : IFileMoveOperations
{
    public List<string> Events { get; } = new();
    public int MoveCalls { get; private set; }
    public int MoveFailuresRemaining { get; set; }

    public long ProbeExclusiveAccess(string filePath)
    {
        Events.Add("probe");
        return 1024;
    }

    public bool FileExists(string filePath) => false;

    public void Delete(string filePath)
    {
        throw new InvalidOperationException("No destination file should be deleted in this fixture.");
    }

    public void Move(string sourcePath, string destinationPath)
    {
        Events.Add("move");
        MoveCalls++;

        if (MoveFailuresRemaining > 0)
        {
            MoveFailuresRemaining--;
            throw new IOException("simulated file lock during move");
        }
    }
}

sealed class QueueWorkItem
{
    public QueueWorkItem(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public WorkStatus Status { get; set; } = WorkStatus.Pending;
}

enum WorkStatus
{
    Pending,
    Failed,
    Completed
}