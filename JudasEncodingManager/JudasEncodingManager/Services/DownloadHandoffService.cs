using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JudasEncodingManager.Services
{
    public sealed record QBittorrentTorrentState(string State);

    /// <summary>
    /// The qBittorrent operations needed to safely hand a downloaded file to
    /// the encoding pipeline.
    /// </summary>
    public interface IQBittorrentStopClient
    {
        Task<QBittorrentTorrentState?> GetTorrentStateAsync(
            string hash,
            CancellationToken cancellationToken);

        Task<bool> StopTorrentAsync(
            string hash,
            CancellationToken cancellationToken);
    }

    public interface IFileHandoff
    {
        Task WaitForExclusiveAccessAsync(
            string filePath,
            CancellationToken cancellationToken);

        /// <summary>
        /// Moves the file only after revalidating exclusive source access for
        /// the current move attempt.
        /// </summary>
        Task MoveAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Confirms that qBittorrent has stopped managing a file and that the file
    /// is exclusively accessible before moving it.
    /// </summary>
    public sealed class DownloadHandoffService
    {
        private readonly IQBittorrentStopClient _qbitClient;
        private readonly IFileHandoff _fileHandoff;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly int _maxStopAttempts;
        private readonly int _maxStateChecks;
        private readonly TimeSpan _stateCheckDelay;
        private readonly TimeSpan _retryDelay;

        public DownloadHandoffService(
            IQBittorrentStopClient qbitClient,
            IFileHandoff? fileHandoff = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null,
            int maxStopAttempts = 3,
            int maxStateChecks = 10,
            TimeSpan? stateCheckDelay = null,
            TimeSpan? retryDelay = null)
        {
            if (maxStopAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxStopAttempts));
            if (maxStateChecks < 1) throw new ArgumentOutOfRangeException(nameof(maxStateChecks));

            _qbitClient = qbitClient ?? throw new ArgumentNullException(nameof(qbitClient));
            _fileHandoff = fileHandoff ?? new LocalFileHandoff();
            _delay = delay ?? Task.Delay;
            _maxStopAttempts = maxStopAttempts;
            _maxStateChecks = maxStateChecks;
            _stateCheckDelay = stateCheckDelay ?? TimeSpan.FromSeconds(1);
            _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
        }

        public async Task StopAndWaitAsync(
            string torrentHash,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(torrentHash))
                throw new InvalidOperationException(
                    "The downloaded torrent has no hash; its stopped state cannot be confirmed.");

            for (var stopAttempt = 1; stopAttempt <= _maxStopAttempts; stopAttempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentInfo = await _qbitClient.GetTorrentStateAsync(
                    torrentHash,
                    cancellationToken);

                if (currentInfo is not null)
                {
                    // A completed torrent may already be paused, in which case
                    // sending /stop is unnecessary. We still perform the
                    // follow-up read below so the state was observed through
                    // the same confirmation path as a stop request.
                    if (!QBittorrentStatePolicy.IsStoppedState(currentInfo.State))
                        await _qbitClient.StopTorrentAsync(torrentHash, cancellationToken);

                    for (var stateCheck = 1; stateCheck <= _maxStateChecks; stateCheck++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var info = await _qbitClient.GetTorrentStateAsync(
                            torrentHash,
                            cancellationToken);
                        if (info is null)
                            break;

                        if (QBittorrentStatePolicy.IsStoppedState(info.State))
                        {
                            await _delay(_stateCheckDelay, cancellationToken);
                            return;
                        }

                        if (stateCheck < _maxStateChecks)
                            await _delay(_stateCheckDelay, cancellationToken);
                    }
                }

                if (stopAttempt < _maxStopAttempts)
                    await _delay(_retryDelay, cancellationToken);
            }

            throw new IOException(
                $"Unable to confirm that qBittorrent stopped torrent {torrentHash}; the source file was not moved.");
        }

        public async Task MoveAfterExclusiveAccessAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            await WaitForExclusiveAccessAsync(sourcePath, cancellationToken);
            await MoveFileWithRetryAsync(sourcePath, destinationPath, cancellationToken);
        }

        public Task WaitForExclusiveAccessAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            return _fileHandoff.WaitForExclusiveAccessAsync(filePath, cancellationToken);
        }

        public Task MoveFileWithRetryAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            return _fileHandoff.MoveAsync(sourcePath, destinationPath, cancellationToken);
        }
    }

    public sealed class LocalFileHandoff : IFileHandoff
    {
        private const int MaxReadyChecks = 12;
        private const int MaxMoveAttempts = 5;
        private readonly IFileMoveOperations _fileOperations;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public LocalFileHandoff()
            : this(new SystemFileMoveOperations(), Task.Delay)
        {
        }

        internal LocalFileHandoff(
            IFileMoveOperations fileOperations,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            _fileOperations = fileOperations ?? throw new ArgumentNullException(nameof(fileOperations));
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        }

        public async Task WaitForExclusiveAccessAsync(
            string filePath,
            CancellationToken cancellationToken)
        {
            long previousSize = -1;
            var stableChecks = 0;
            Exception? lastException = null;

            for (var attempt = 1; attempt <= MaxReadyChecks; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var currentSize = _fileOperations.ProbeExclusiveAccess(filePath);

                    if (currentSize > 0 && currentSize == previousSize)
                        stableChecks++;
                    else
                        stableChecks = 0;

                    previousSize = currentSize;

                    if (stableChecks >= 1)
                        return;
                }
                catch (IOException ex)
                {
                    lastException = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastException = ex;
                }

                await _delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            throw new IOException(
                $"The downloaded file is still in use by another process after {MaxReadyChecks} seconds: {filePath}",
                lastException);
        }

        public async Task MoveAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            Exception? lastException = null;

            for (var attempt = 1; attempt <= MaxMoveAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // The readiness probe above is intentionally repeated for
                    // every move attempt. A handle may be reacquired after the
                    // file was first found stable, especially between retries.
                    _fileOperations.ProbeExclusiveAccess(sourcePath);

                    if (_fileOperations.FileExists(destinationPath))
                        _fileOperations.Delete(destinationPath);

                    _fileOperations.Move(sourcePath, destinationPath);
                    return;
                }
                catch (IOException ex)
                {
                    lastException = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastException = ex;
                }

                if (attempt < MaxMoveAttempts)
                    await _delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }

            throw new IOException(
                $"Could not move the downloaded file after {MaxMoveAttempts} attempts: {Path.GetFileName(sourcePath)}",
                lastException);
        }

    }

    internal interface IFileMoveOperations
    {
        long ProbeExclusiveAccess(string filePath);
        bool FileExists(string filePath);
        void Delete(string filePath);
        void Move(string sourcePath, string destinationPath);
    }

    internal sealed class SystemFileMoveOperations : IFileMoveOperations
    {
        public long ProbeExclusiveAccess(string filePath)
        {
            using var file = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);
            return file.Length;
        }

        public bool FileExists(string filePath) => File.Exists(filePath);

        public void Delete(string filePath) => File.Delete(filePath);

        public void Move(string sourcePath, string destinationPath) =>
            File.Move(sourcePath, destinationPath);
    }
}