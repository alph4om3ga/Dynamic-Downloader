using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;

namespace JudasEncodingManager.Services
{
    public class FtpService
    {
        private string _host = "";
        private int _port = 21;
        private string _username = "";
        private string _password = "";
        private string _releasesPath = "Releases/";
        private string _torrentsPath = "TorrentFiles/";
        
        private CancellationTokenSource? _uploadCts;
        private bool _isCancelled;
        private AsyncFtpClient? _activeClient;

        public event EventHandler<double>? ProgressChanged;
        public event EventHandler<string>? StatusChanged;

        public void Configure(string host, string username, string password, string releasesPath, string torrentsPath)
        {
            _host = host;
            
            // Parse port if included in host (e.g., "192.168.1.1:2121")
            if (host.Contains(":"))
            {
                var parts = host.Split(':');
                _host = parts[0];
                if (int.TryParse(parts[1], out int port))
                {
                    _port = port;
                }
            }
            
            _releasesPath = releasesPath;
            _torrentsPath = torrentsPath;
            _username = username;
            _password = password;
            
            StatusChanged?.Invoke(this, $"FTP configured: {_host}:{_port}");
        }

        public void Cancel()
        {
            _isCancelled = true;
            _uploadCts?.Cancel();

            // Forcefully disconnect any active client
            Task.Run(async () =>
            {
                try
                {
                    if (_activeClient != null && _activeClient.IsConnected)
                    {
                        await _activeClient.Disconnect();
                    }
                    _activeClient?.Dispose();
                    _activeClient = null;
                }
                catch
                {
                    // Swallow disconnect errors during cancellation
                }
            });

            StatusChanged?.Invoke(this, "⛔ Upload cancelled by user");
        }

        public async Task<FtpUploadResult> UploadTorrentFileAsync(string localPath, string remoteFileName, CancellationToken cancellationToken)
        {
            var remotePath = $"{_torrentsPath}{remoteFileName}";
            return await UploadWithRetryAsync(localPath, remotePath, 3, cancellationToken);
        }

        public async Task<FtpUploadResult> UploadDescriptionFileAsync(string localPath, string remoteFileName, CancellationToken cancellationToken)
        {
            var remotePath = $"{_torrentsPath}{remoteFileName}";
            return await UploadWithRetryAsync(localPath, remotePath, 3, cancellationToken);
        }

        public async Task<FtpUploadResult> UploadEpisodeAsync(string localPath, string showFolder, string remoteFileName, CancellationToken cancellationToken)
        {
            var remotePath = $"{_releasesPath}{showFolder}/{remoteFileName}";
            return await UploadWithRetryAsync(localPath, remotePath, 5, cancellationToken);
        }

        private async Task<FtpUploadResult> UploadWithRetryAsync(string localPath, string remotePath, int maxRetries, CancellationToken cancellationToken)
        {
            var result = new FtpUploadResult { LocalPath = localPath, RemotePath = remotePath };
            _isCancelled = false;
            _uploadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            if (!File.Exists(localPath))
            {
                result.Success = false;
                result.ErrorMessage = $"Local file not found: {localPath}";
                StatusChanged?.Invoke(this, $"❌ {result.ErrorMessage}");
                return result;
            }
            
            var fileInfo = new FileInfo(localPath);
            var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);
            
            StatusChanged?.Invoke(this, $"📤 Starting upload: {Path.GetFileName(localPath)} ({fileSizeMB:F2} MB)");
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (_isCancelled || _uploadCts.Token.IsCancellationRequested)
                {
                    result.Success = false;
                    result.ErrorMessage = "Upload cancelled";
                    return result;
                }
                
                result.Attempts = attempt;
                
                try
                {
                    StatusChanged?.Invoke(this, $"📤 Upload attempt {attempt}/{maxRetries}: {Path.GetFileName(localPath)}");
                    
                    var success = await UploadFileAsync(localPath, remotePath, _uploadCts.Token);
                    
                    if (success)
                    {
                        result.Success = true;
                        StatusChanged?.Invoke(this, $"✅ Upload complete: {Path.GetFileName(localPath)}");
                        return result;
                    }
                    else if (_isCancelled)
                    {
                        result.Success = false;
                        result.ErrorMessage = "Upload cancelled";
                        return result;
                    }
                }
                catch (OperationCanceledException)
                {
                    result.Success = false;
                    result.ErrorMessage = "Upload cancelled";
                    StatusChanged?.Invoke(this, $"⛔ Upload cancelled");
                    return result;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                    StatusChanged?.Invoke(this, $"❌ Upload error: {ex.Message}");
                }

                if (attempt < maxRetries && !_isCancelled)
                {
                    StatusChanged?.Invoke(this, $"⏳ Upload failed, waiting 30 seconds before retry...");
                    
                    // Wait 30 seconds before retry
                    for (int i = 30; i > 0; i--)
                    {
                        if (_isCancelled || _uploadCts.Token.IsCancellationRequested)
                        {
                            result.ErrorMessage = "Upload cancelled";
                            result.Success = false;
                            return result;
                        }
                        
                        if (i % 10 == 0)
                        {
                            StatusChanged?.Invoke(this, $"⏳ Retrying in {i} seconds...");
                        }
                        
                        try
                        {
                            await Task.Delay(1000, _uploadCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            result.ErrorMessage = "Upload cancelled";
                            result.Success = false;
                            return result;
                        }
                    }
                }
            }

            result.Success = false;
            if (string.IsNullOrEmpty(result.ErrorMessage))
            {
                result.ErrorMessage = "Max retries exceeded";
            }
            
            return result;
        }

        private async Task<bool> UploadFileAsync(string localPath, string remotePath, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(localPath);
            var totalBytes = fileInfo.Length;
            var startTime = DateTime.Now;
            var lastProgressUpdate = DateTime.Now;
            
            StatusChanged?.Invoke(this, $"🔗 Connecting to ftp://{_host}:{_port}/...");
            
            _activeClient = new AsyncFtpClient(_host, _username, _password, _port);
            _activeClient.Config.ConnectTimeout = 30000;
            _activeClient.Config.ReadTimeout = 60000;
            _activeClient.Config.DataConnectionConnectTimeout = 30000;
            _activeClient.Config.DataConnectionReadTimeout = 120000;
            
            try
            {
                await _activeClient.Connect(cancellationToken);
                
                if (_isCancelled || cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                
                StatusChanged?.Invoke(this, $"✅ Connected to FTP server");
                
                // Create directory if needed
                var remoteDir = Path.GetDirectoryName(remotePath)?.Replace("\\", "/");
                if (!string.IsNullOrEmpty(remoteDir))
                {
                    StatusChanged?.Invoke(this, $"📁 Ensuring directory exists: {remoteDir}");
                    await _activeClient.CreateDirectory(remoteDir, true, cancellationToken);
                }
                
                if (_isCancelled || cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                
                StatusChanged?.Invoke(this, $"📤 Uploading to {remotePath}...");
                
                // Use progress callback
                var progress = new Progress<FtpProgress>(p =>
                {
                    if (_isCancelled) return;
                    
                    ProgressChanged?.Invoke(this, p.Progress / 100.0);
                    
                    // Log progress every 5 seconds
                    if ((DateTime.Now - lastProgressUpdate).TotalSeconds >= 5)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var uploadedBytes = (long)(totalBytes * p.Progress / 100.0);
                        var speed = elapsed.TotalSeconds > 0 ? uploadedBytes / elapsed.TotalSeconds / (1024 * 1024) : 0;
                        var uploadedMB = uploadedBytes / (1024.0 * 1024.0);
                        var totalMB = totalBytes / (1024.0 * 1024.0);
                        var remainingBytes = totalBytes - uploadedBytes;
                        var eta = speed > 0 ? TimeSpan.FromSeconds(remainingBytes / (speed * 1024 * 1024)) : TimeSpan.Zero;
                        
                        StatusChanged?.Invoke(this, $"📤 {uploadedMB:F1}/{totalMB:F1} MB ({p.Progress:F0}%) - {speed:F2} MB/s - ETA: {eta:mm\\:ss}");
                        lastProgressUpdate = DateTime.Now;
                    }
                });
                
                var status = await _activeClient.UploadFile(
                    localPath, 
                    remotePath, 
                    FtpRemoteExists.Overwrite, 
                    true,  // createRemoteDir 
                    FtpVerify.None,
                    progress,
                    cancellationToken);
                
                if (_isCancelled || cancellationToken.IsCancellationRequested)
                {
                    StatusChanged?.Invoke(this, $"⛔ Upload aborted");
                    return false;
                }
                
                if (status == FtpStatus.Success)
                {
                    var elapsed = DateTime.Now - startTime;
                    var avgSpeed = totalBytes / elapsed.TotalSeconds / (1024 * 1024);
                    StatusChanged?.Invoke(this, $"✅ Upload finished in {elapsed:mm\\:ss} ({avgSpeed:F2} MB/s avg)");
                    return true;
                }
                else
                {
                    StatusChanged?.Invoke(this, $"❌ Upload failed with status: {status}");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, $"⛔ Upload cancelled");
                throw;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"❌ FTP error: {ex.Message}");
                throw;
            }
            finally
            {
                try
                {
                    if (_activeClient.IsConnected)
                    {
                        await _activeClient.Disconnect(cancellationToken);
                    }
                }
                catch { }
                
                _activeClient.Dispose();
                _activeClient = null;
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                StatusChanged?.Invoke(this, $"🔗 Testing FTP connection to {_host}:{_port}...");
                
                using var client = new AsyncFtpClient(_host, _username, _password, _port);
                client.Config.ConnectTimeout = 10000;
                
                await client.Connect();
                var connected = client.IsConnected;
                
                if (connected)
                {
                    var workingDir = await client.GetWorkingDirectory();
                    StatusChanged?.Invoke(this, $"✅ FTP connection successful (Working dir: {workingDir})");
                    await client.Disconnect();
                }
                else
                {
                    StatusChanged?.Invoke(this, $"❌ FTP connection failed");
                }
                
                return connected;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"❌ FTP connection failed: {ex.Message}");
                return false;
            }
        }
    }

    public class FtpUploadResult
    {
        public bool Success { get; set; }
        public string LocalPath { get; set; } = "";
        public string RemotePath { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public int Attempts { get; set; }
    }
}
