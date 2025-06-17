using AWS2.FolderWatcherService.Models;
using AWS2.FolderWatcherService.Helpers;
using System.Net;
using System.Text;
using System.Text.Json;
using AWS2.FolderWatcherService.Services;
using Renci.SshNet;
using System.Threading;
using System.Collections.Concurrent;

namespace AWS2.FolderWatcherService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _config;
        private readonly List<FileSystemWatcher> _watchers = new();
        //private readonly string _hostName = Dns.GetHostName();
        private readonly string _hostName = "Demo";

        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly INotificationService _notificationService;
        private readonly Dictionary<string, DateTime> _lastProcessedFiles = new();
        private readonly TimeSpan _changeEventSuppressWindow = TimeSpan.FromSeconds(2);
        private readonly ConcurrentDictionary<string, DateTime> _recentEvents = new();
        private readonly TimeSpan _eventSuppressionWindow = TimeSpan.FromSeconds(2);

        // Add these fields to Worker class
        private string? _configApiUrl;
        private List<WatchedFolder>? _foldersToWatch;
        private DateTime _lastConfigFetchTime = DateTime.MinValue;
        private readonly TimeSpan _configRefreshInterval = TimeSpan.FromMinutes(5);
        private readonly object _watcherLock = new();


        private DateTime _lastStatsLogTime = DateTime.MinValue;
        // Day-wise statistics tracking
        private DateTime _currentDay;
        private int _totalFilesProcessedToday;
        private int _filesWithIssuesToday;
        private readonly object _statsLock = new object();

        private readonly List<FileProcessingLogModal> _processingLogs = new();

        public Worker(ILogger<Worker> logger, IConfiguration config, INotificationService notificationService)
        {
            _logger = logger;
            _config = config;
            _notificationService = notificationService;
            _currentDay = DateTime.Today;
        }



        // Add this method to Worker class
        private async Task<List<WatchedFolder>?> FetchWatchedFoldersFromApiAsync()
        {
            try
            {
                string configIds = _config.GetValue<string>("ConfigIds") ?? string.Empty;
                if (string.IsNullOrEmpty(configIds))
                {
                    await MessageLoggerHelper.LogWarningAsync("ConfigIds not configured.", _logger, _notificationService);
                    return null;
                }
                _configApiUrl ??= $"{APIURLList.BaseURL}{APIURLList.GetFileWatcherConfigAPI}";
                if (string.IsNullOrEmpty(_configApiUrl))
                {
                    await MessageLoggerHelper.LogWarningAsync("WatchedFoldersApiUrl not configured.", _logger, _notificationService);
                    return null;
                }

                using var httpClient = new HttpClient();
                using var formContent = new MultipartFormDataContent();
                formContent.Add(new StringContent(configIds), "configIds");

                var response = await httpClient.PostAsync(_configApiUrl, formContent);
                if (!response.IsSuccessStatusCode)
                {
                    await MessageLoggerHelper.LogWarningAsync($"Failed to fetch watched folders from API: {response.StatusCode}", _logger, _notificationService);
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();

                var responseApiResult = JsonSerializer.Deserialize<ApiResultModal>(responseContent, _jsonOptions) ?? new ApiResultModal { IsSuccess = false };
                if (!responseApiResult.IsSuccess)
                {
                    await MessageLoggerHelper.LogWarningAsync($"{responseApiResult.Message}", _logger, _notificationService);
                }

                var folders = JsonSerializer.Deserialize<List<WatchedFolder>>(responseApiResult.Result?.ToString() ?? string.Empty, _jsonOptions);

                return folders;
            }
            catch (Exception ex)
            {
                await MessageLoggerHelper.LogErrorAsync(ex, "Error fetching watched folders from API", _logger, _notificationService);
                return null;
            }
        }

        // Add this method to Worker class
        private async Task RefreshWatchersIfNeededAsync(CancellationToken stoppingToken)
        {
            if ((DateTime.Now - _lastConfigFetchTime) < _configRefreshInterval) return;

            var newFolders = await FetchWatchedFoldersFromApiAsync();
            if (newFolders is null) return;

            // Compare with current config
            bool configChanged = _foldersToWatch == null ||
                                 newFolders.Count != _foldersToWatch.Count ||
                                 !newFolders.SequenceEqual(_foldersToWatch, new WatchedFolderComparer());

            if (configChanged)
            {
                lock (_watcherLock)
                {
                    foreach (var watcher in _watchers)
                        watcher.Dispose();
                    _watchers.Clear();
                }
                await InitializeWatchersAsync(newFolders);
                _foldersToWatch = newFolders;
                await MessageLoggerHelper.LogMessageAsync("Folder watcher configuration refreshed from API.", _logger, _notificationService);
            }

            _lastConfigFetchTime = DateTime.Now;
        }

        // Add this class to Worker.cs (for comparing WatchedFolder objects)
        private class WatchedFolderComparer : IEqualityComparer<WatchedFolder>
        {
            public bool Equals(WatchedFolder? x, WatchedFolder? y)
            {
                if (x == null || y == null) return false;
                return x.FolderPath == y.FolderPath &&
                       x.ArchiveFolderPath == y.ArchiveFolderPath &&
                       x.ClientCode == y.ClientCode &&
                       x.IncludeSubDirectories == y.IncludeSubDirectories &&
                       x.CopyFileForOtherServer == y.CopyFileForOtherServer &&
                       x.CopyFileFtpServerName == y.CopyFileFtpServerName &&
                       x.CopyFileFtpUsername == y.CopyFileFtpUsername &&
                       x.CopyFileFtpPassword == y.CopyFileFtpPassword &&
                       x.CopyFileFtpAccessDirectory == y.CopyFileFtpAccessDirectory &&
                       x.CopyFileSecure == y.CopyFileSecure &&
                       x.CopyFileFtpPort == y.CopyFileFtpPort &&
                       x.EnableMovingForArchiveFolder == y.EnableMovingForArchiveFolder;
            }

            public int GetHashCode(WatchedFolder obj)
            {
                return HashCode.Combine(obj.FolderPath, obj.ClientCode, obj.IncludeSubDirectories, obj.EnableMovingForArchiveFolder);
            }
        }

        // Replace ExecuteAsync with this version
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await MessageLoggerHelper.LogMessageAsync("Starting folder watcher service...", _logger, _notificationService);

                //_foldersToWatch = _config.GetSection("WatchedFolders").Get<List<WatchedFolder>>();

                _foldersToWatch = await FetchWatchedFoldersFromApiAsync();
                if (_foldersToWatch is null || !_foldersToWatch.Any())
                {
                    await MessageLoggerHelper.LogWarningAsync("No folders configured for watching", _logger, _notificationService);
                    return;
                }

                await InitializeWatchersAsync(_foldersToWatch);

                while (!stoppingToken.IsCancellationRequested)
                {
                    // Check if config needs to be refreshed from API
                    await RefreshWatchersIfNeededAsync(stoppingToken);

                    // Check if day has changed and reset counters if needed
                    CheckAndResetDayCounters();

                    // Log statistics periodically (e.g., every hour)
                    await LogStatisticsIfNeeded();

                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                await ExceptionLoggerHelper.LogExceptionAsync(ex, _notificationService);
            }
            finally
            {
                await MessageLoggerHelper.LogWarningAsync("Stopping folder watcher service...", _logger, _notificationService);
            }
        }

        private void CheckAndResetDayCounters()
        {
            DateTime today = DateTime.Today;
            if (today <= _currentDay) return;

            lock (_statsLock)
            {
                if (today <= _currentDay) return;

                // Only send notification if there were issues
                if (_filesWithIssuesToday != 0)
                {
                    var warningEmail = new WarningEmailModal
                    {
                        Timestamp = _currentDay,
                        TotalFilesProcessed = _totalFilesProcessedToday,
                        FilesWithIssues = _filesWithIssuesToday,
                        FileProcessingLog = _processingLogs
                    };

                    _ = _notificationService.SendWarningNotification(warningEmail)
                        .ContinueWith(t =>
                        {
                            if (!t.IsFaulted)
                            {
                                // Write warning email details to a txt file for auditing
                                try
                                {
                                    var logDir = Path.Combine(AppContext.BaseDirectory, "WarningEmailsLogs");
                                    if (!Directory.Exists(logDir))
                                        Directory.CreateDirectory(logDir);

                                    var fileName = $"WarningEmail_{_currentDay:yyyyMMdd}.txt";
                                    var filePath = Path.Combine(logDir, fileName);

                                    var logContent = new StringBuilder();
                                    logContent.AppendLine($"Timestamp: {warningEmail.Timestamp}");
                                    logContent.AppendLine($"TotalFilesProcessed: {warningEmail.TotalFilesProcessed}");
                                    logContent.AppendLine($"FilesWithIssues: {warningEmail.FilesWithIssues}");
                                    logContent.AppendLine("FileProcessingLog:");
                                    foreach (var log in warningEmail.FileProcessingLog)
                                    {
                                        logContent.AppendLine($"  - [{log.Timestamp:yyyy-MM-dd HH:mm:ss}] Name: {log.Name}, Path: {log.Path}, FileName: {log.FileName}, Details: {log.Details}");
                                    }

                                    File.WriteAllText(filePath, logContent.ToString());
                                }
                                catch (Exception fileEx)
                                {
                                    _logger.LogError(fileEx, "Failed to write warning email details to txt file.");
                                }
                            }
                        });
                }

                // Reset counters
                _currentDay = today;
                _totalFilesProcessedToday = 0;
                _filesWithIssuesToday = 0;
                _processingLogs.Clear();
            }
        }

        private void LogProcessingIssue(string fileName, string details, string name, string path)
        {
            lock (_statsLock)
            {
                _processingLogs.Add(new FileProcessingLogModal
                {
                    Timestamp = DateTime.Now,
                    Name = name,
                    Path = path,
                    FileName = fileName,
                    Details = details
                });
            }
        }

        private async Task LogStatisticsIfNeeded()
        {
            // Log statistics every hour, but only once per hour
            var now = DateTime.Now;
            if (now.Minute == 0 && (now - _lastStatsLogTime).TotalMinutes >= 60)
            {
                await LogDailyStatistics();
                _lastStatsLogTime = now;
            }
        }

        private async Task LogDailyStatistics()
        {
            string statsMessage = $"Daily Statistics (as of {DateTime.Now:HH:mm}): " +
                                 $"Total Files Processed: {_totalFilesProcessedToday}, " +
                                 $"Files With Issues: {_filesWithIssuesToday}";

            await MessageLoggerHelper.LogMessageAsync(statsMessage, _logger, _notificationService);
        }

        private void IncrementFilesProcessed()
        {
            lock (_statsLock)
            {
                _totalFilesProcessedToday++;
            }
        }

        private void IncrementFilesWithIssues(string fileName, string details, string name, string path)
        {
            lock (_statsLock)
            {
                _filesWithIssuesToday++;
                LogProcessingIssue(fileName, details, name, path);
            }
        }

        private async Task InitializeWatchersAsync(List<WatchedFolder> folders)
        {
            var initTasks = folders.Select(async folder =>
            {
                try
                {
                    if (!Directory.Exists(folder.FolderPath))
                    {
                        await MessageLoggerHelper.LogWarningAsync($"Directory {folder.FolderPath} does not exist. Creating...", _logger, _notificationService);
                        Directory.CreateDirectory(folder.FolderPath);
                    }

                    var watcher = new FileSystemWatcher(folder.FolderPath)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        IncludeSubdirectories = folder.IncludeSubDirectories,
                        EnableRaisingEvents = true,
                        InternalBufferSize = 64 * 1024
                    };

                    watcher.Changed += async (sender, e) =>
                    {
                        try
                        {
                            _ = Task.Run(() => OnFileEvent(sender, e, "Changed", folder));
                        }
                        catch (Exception ex)
                        {
                            await MessageLoggerHelper.LogErrorAsync(ex, "Error starting file event task", _logger, _notificationService);
                        }
                    };

                    watcher.Error += async (sender, e) => await OnError(sender, e);

                    lock (_watcherLock)
                    {
                        _watchers.Add(watcher);
                    }

                    await MessageLoggerHelper.LogMessageAsync($"Started watching folder: {folder.FolderPath}", _logger, _notificationService);
                }
                catch (Exception ex)
                {
                    await ExceptionLoggerHelper.LogExceptionAsync(ex, _notificationService);
                }
            });

            await Task.WhenAll(initTasks);
        }

        private async Task OnFileEvent(object sender, FileSystemEventArgs e, string eventType, WatchedFolder folderConfig)
        {
            try
            {
                if (eventType == "Changed" && IsDuplicateEvent(e.FullPath))
                    return;

                if (!File.Exists(e.FullPath)) return;

                // Accept only .csv files
                if (!e.FullPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    return;

                IncrementFilesProcessed();
                await MessageLoggerHelper.LogMessageAsync($"File {eventType}: {e.FullPath}", _logger, _notificationService);

                await ProcessFile(e.Name ?? string.Empty, e.FullPath, folderConfig);

                // Process all files in the directory after an event
                //await MessageLoggerHelper.LogMessageAsync($"Process all files", _logger, _notificationService);
                //var files = Directory.GetFiles(folderConfig.FolderPath);
                //foreach (var file in files)
                //{
                //    var fileName = Path.GetFileName(file);
                //    await ProcessFile(fileName, file, folderConfig);
                //}
                //await MessageLoggerHelper.LogMessageAsync($"Completed process all files", _logger, _notificationService);
            }
            catch (Exception ex)
            {
                await MessageLoggerHelper.LogErrorAsync(ex, $"Error processing file event: {e.FullPath}", _logger, _notificationService);
            }
        }

        private bool IsDuplicateEvent(string path)
        {
            lock (_lastProcessedFiles)
            {
                if (_lastProcessedFiles.TryGetValue(path, out var lastTime))
                {
                    if ((DateTime.Now - lastTime) < _changeEventSuppressWindow)
                    {
                        // Duplicate event, skip processing
                        return true;
                    }
                }
                _lastProcessedFiles[path] = DateTime.Now;
            }
            return false;
        }

        private async Task<string?> ReadFileWithRetryAsync(string filePath, int maxAttempts = 5, int initialDelayMs = 200, int timeoutSeconds = 30)
        {

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    try
                    {
                        using (var stream = new FileStream(
                            filePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite,
                            bufferSize: 4096,
                            useAsync: true))
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                return await reader.ReadToEndAsync(cts.Token);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // Timeout expired
                    }
                    catch (IOException) when (attempt < maxAttempts - 1)
                    {
                        var delayMs = initialDelayMs * (int)Math.Pow(2, attempt);
                        await Task.Delay(delayMs, cts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout expired
                return null;
            }

            return null;
        }

        private async Task<ApiResultModal> CallApiAsync(string apiUrl, string content)
        {
            using (HttpClient httpClient = new HttpClient())
            {

                var requestContent = new StringContent(content, Encoding.UTF8, "text/plain");
                var response = await httpClient.PostAsync(apiUrl, requestContent).ConfigureAwait(false);

                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    await MessageLoggerHelper.LogWarningAsync($"API call failed: {response.StatusCode} - {responseContent}", _logger, _notificationService);
                    return new ApiResultModal { IsSuccess = false };
                }

                try
                {
                    return JsonSerializer.Deserialize<ApiResultModal>(responseContent, _jsonOptions) ?? new ApiResultModal { IsSuccess = false };
                }
                catch (JsonException ex)
                {
                    await MessageLoggerHelper.LogErrorAsync(ex, "Failed to deserialize API response", _logger, _notificationService);
                    return new ApiResultModal { IsSuccess = false };
                }
            }
        }

        public async Task<bool> CallApiStoreFileLogsDataAsync(string apiUrl, List<string> DataContent)
        {
            try
            {
                using (HttpClient httpClient = new HttpClient())
                {
                    string jsonPayload = System.Text.Json.JsonSerializer.Serialize(DataContent); // Fixed incorrect 'JsonConvertt' to 'JsonSerializer'  
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var response = await httpClient.PostAsync(apiUrl, content).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        await MessageLoggerHelper.LogWarningAsync($"Error in file log API call -> {error}", _logger, _notificationService);
                        return false;
                    }

                    var contents = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var finalResponse = JsonSerializer.Deserialize<ApiResultModal>(contents, _jsonOptions);

                    if (finalResponse is null || !finalResponse.IsSuccess || finalResponse.StatusCode != 200)
                    {
                        await MessageLoggerHelper.LogWarningAsync("File log API response was not successful.", _logger, _notificationService);
                        return false;
                    }

                    await MessageLoggerHelper.LogMessageAsync("File log API response successful.", _logger, _notificationService);
                    return true;
                }
            }
            catch (Exception ex)
            {
                await MessageLoggerHelper.LogErrorAsync(ex, $"Error processing file -> {ex.Message}", _logger, _notificationService);
                return false;
            }
        }

        public async Task<bool> CallApiCheckFileNameLogsAsync(string apiUrl)
        {
            try
            {
                using (HttpClient httpClient = new HttpClient())
                {

                    var response = await httpClient.PostAsync(apiUrl, null).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        await MessageLoggerHelper.LogWarningAsync($"Error in file name check API call -> {error}", _logger, _notificationService);
                        return false;
                    }

                    var contents = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var finalResponse = JsonSerializer.Deserialize<ApiResultModal>(contents, _jsonOptions);

                    if (finalResponse is null || !finalResponse.IsSuccess || finalResponse.StatusCode != 200)
                    {
                        await MessageLoggerHelper.LogWarningAsync("File name check API response was not successful.", _logger, _notificationService);
                        return false;
                    }

                    await MessageLoggerHelper.LogMessageAsync("File name check API response successful.", _logger, _notificationService);
                    return Convert.ToBoolean(finalResponse?.Result.ToString());
                }
            }
            catch (Exception ex)
            {
                await MessageLoggerHelper.LogErrorAsync(ex, $"Error file name check api -> {ex.Message}", _logger, _notificationService);
                return false;
            }
        }

        public async Task MoveFile(string sourcePath, WatchedFolder folder)
        {
            if (string.IsNullOrEmpty(folder.ArchiveFolderPath))
            {
                await MessageLoggerHelper.LogWarningAsync("ArchiveFilePath is null. Cannot move file.", _logger, _notificationService);
                return;
            }

            if (!Directory.Exists(folder.ArchiveFolderPath))
            {
                //await MessageLoggerHelper.LogWarningAsync($"Archive directory {folder.ArchiveFilePath} does not exist. Creating...", _logger, _notificationService);
                Directory.CreateDirectory(folder.ArchiveFolderPath);
            }

            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(folder.ArchiveFolderPath, fileName);

            try
            {
                if (File.Exists(destPath))
                {
                    File.Delete(sourcePath);
                    return;
                }

                File.Move(sourcePath, destPath);
                await MessageLoggerHelper.LogMessageAsync($"Moved file from {sourcePath} to {destPath}", _logger, _notificationService);
            }
            catch (Exception ex)
            {
                await MessageLoggerHelper.LogErrorAsync(ex, $"Failed to move file from {sourcePath} to {destPath}", _logger, _notificationService);
            }
        }

        private async Task OnError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            _logger.LogError(ex, "File system watcher error occurred");
            await ExceptionLoggerHelper.LogExceptionAsync(ex, _notificationService);
        }

        private async Task ProcessFile(string fileName, string fullPath, WatchedFolder folderConfig)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return;

                var apiURL = $"{APIURLList.BaseURL}{APIURLList.CheckFileNameLogsAPI}"
                    .Replace("{clientCode}", folderConfig.ClientCode)
                    .Replace("{fileName}", fileName);

                bool isFileNameFound = await CallApiCheckFileNameLogsAsync(apiURL);

                if (isFileNameFound)
                {
                    IncrementFilesWithIssues(fileName, $"{fileName} -- file already processed", folderConfig.Name ?? "UnknownName", folderConfig.FolderPath);
                    await MessageLoggerHelper.LogWarningAsync($"{fileName} -- file already processed", _logger, _notificationService);
                    return;
                }

                var processedFiles = new List<string>();
                if (!string.IsNullOrEmpty(fileName)) processedFiles.Add(fileName);

                if (!processedFiles.Any())
                {
                    IncrementFilesWithIssues(fileName ?? "UnknownFile", "File Not Exist", folderConfig.Name ?? "UnknownName", folderConfig.FolderPath);
                    return;
                }

                try
                {
                    string apiUrlStoreFiles = $"{APIURLList.BaseURL}{APIURLList.ReceivesFileLogsAPI}"
                               .Replace("{clientCode}", folderConfig.ClientCode)
                               .Replace("{hostDetail}", _hostName);

                    var storeResponse = await CallApiStoreFileLogsDataAsync(apiUrlStoreFiles, processedFiles);
                    if (!storeResponse)
                    {
                        await MessageLoggerHelper.LogWarningAsync($"Error storing file logs", _logger, _notificationService);
                    }
                }
                catch (Exception ex)
                {
                    await MessageLoggerHelper.LogErrorAsync(ex, $"Exception storing file logs -> {ex.Message}", _logger, _notificationService);
                }


                // Process file content
                var fileContent = await ReadFileWithRetryAsync(fullPath);
                if (string.IsNullOrEmpty(fileContent))
                {
                    IncrementFilesWithIssues(fileName ?? "UnknownFile", "File content is empty or null for file", folderConfig.Name ?? "UnknownName", folderConfig.FolderPath);
                    await MessageLoggerHelper.LogWarningAsync($"File content is empty or null for file: {fullPath}", _logger, _notificationService);
                    return;
                }

                // Prepare API URL
                var apiUrl = $"{APIURLList.BaseURL}{APIURLList.ReceivesStationEnvDataAPI}"
                            .Replace("{clientCode}", folderConfig.ClientCode)
                            .Replace("{transMode}", "GPRS")
                            .Replace("{hostDetail}", _hostName);

                // Call API
                var response = await CallApiAsync(apiUrl, fileContent);
                if (!response.IsSuccess)
                {
                    IncrementFilesWithIssues(fileName ?? "UnknownFile", response.Message ?? "Unknown error", folderConfig.Name ?? "UnknownName", folderConfig.FolderPath);
                    await MessageLoggerHelper.LogWarningAsync($"{response.Message}: {fileName}", _logger, _notificationService);
                    return;
                }

                // FTP copy if enabled
                if (folderConfig.CopyFileForOtherServer && !string.IsNullOrEmpty(folderConfig.CopyFileFtpServerName))
                {
                    await CopyFileToFtpAsync(fullPath, folderConfig);
                }

                if (folderConfig.EnableMovingForArchiveFolder)
                {
                    await MoveFile(fullPath, folderConfig);
                }

                await MessageLoggerHelper.LogMessageAsync($"Complete processing: {fileName}", _logger, _notificationService);
                return;
            }
            catch (Exception ex)
            {
                await MessageLoggerHelper.LogErrorAsync(ex, $"Exception storing file logs -> {ex.Message}", _logger, _notificationService);
                return;
            }
        }

        private async Task CopyFileToFtpAsync(string filePath, WatchedFolder folderConfig)
        {
            string fileName = Path.GetFileName(filePath);

            try
            {
                // Prepare FTP connection details
                var ftpServer = folderConfig.CopyFileFtpServerName;
                var ftpUser = folderConfig.CopyFileFtpUsername ?? string.Empty;
                var ftpPass = folderConfig.CopyFileFtpPassword ?? string.Empty;
                var ftpDir = (folderConfig.CopyFileFtpAccessDirectory ?? "/").TrimEnd('/');
                var ftpPort = folderConfig.CopyFileFtpPort;
                var useSsl = folderConfig.CopyFileSecure;

                var ftpUri = new Uri($"{ftpServer}:{ftpPort}/{ftpDir}/{fileName}");

                if (ftpUri.Scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase) || ftpUri.Scheme.Equals("ftps", StringComparison.OrdinalIgnoreCase))
                {
                    // Configure common request settings
                    void ConfigureRequest(FtpWebRequest request)
                    {
                        request.Credentials = new NetworkCredential(ftpUser, ftpPass);
                        request.EnableSsl = useSsl;
                        request.UseBinary = true;
                        request.UsePassive = true;
                    }

                    // Check if file exists on FTP (only if needed)
                    if (!await CheckFtpFileExistsAsync(ftpUri, ConfigureRequest))
                    {
                        await UploadFileToFtpAsync(filePath, ftpUri, ConfigureRequest);
                        await MessageLoggerHelper.LogMessageAsync($"FTP uploaded: {fileName}", _logger, _notificationService);
                        return;
                    }
                    await MessageLoggerHelper.LogMessageAsync($"FTP file already exists, skipping upload: {fileName}", _logger, _notificationService);
                    return;
                }

                if (ftpUri.Scheme.Equals("sftp", StringComparison.OrdinalIgnoreCase))
                {
                    // Build the remote SFTP path
                    var remotePath = $"{(folderConfig.CopyFileFtpAccessDirectory ?? "/").TrimEnd('/')}/{fileName}";

                    // Use actual credentials from config
                    var sftpCreds = new NetworkCredential(
                        folderConfig.CopyFileFtpUsername ?? string.Empty,
                        folderConfig.CopyFileFtpPassword ?? string.Empty
                    );

                    // Pass remotePath as the third argument, and null for configureClient
                    if (!await CheckSftpFileExistsAsync(ftpUri, sftpCreds, remotePath))
                    {
                        await UploadFileToSftpAsync(filePath, ftpUri, sftpCreds, remotePath);
                        await MessageLoggerHelper.LogMessageAsync($"SFTP uploaded: {fileName}", _logger, _notificationService);
                        return;

                    }
                    await MessageLoggerHelper.LogMessageAsync($"SFTP file already exists, skipping upload: {fileName}", _logger, _notificationService);
                    return;
                }
                throw new NotSupportedException($"Unsupported protocol: {ftpUri.Scheme}");
            }
            catch (Exception ex)
            {
                IncrementFilesWithIssues(fileName, $"FTP copy failed: {ex.Message}", folderConfig.Name, folderConfig.FolderPath);
                await MessageLoggerHelper.LogErrorAsync(ex, $"FTP copy failed for file: {filePath}", _logger, _notificationService);
            }
        }

        private async Task<bool> CheckFtpFileExistsAsync(Uri ftpUri, Action<FtpWebRequest> configureRequest)
        {
            try
            {
                var checkRequest = (FtpWebRequest)WebRequest.Create(ftpUri);
                checkRequest.Method = WebRequestMethods.Ftp.GetFileSize;
                configureRequest(checkRequest);

                using (var checkResponse = (FtpWebResponse)await checkRequest.GetResponseAsync())
                {
                    return true;
                }
            }
            catch (WebException ex) when (ex.Response is FtpWebResponse ftpResponse &&
                                         ftpResponse.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
            {
                return false;
            }
            catch (WebException ex)
            {
                throw new Exception($"FTP check failed: {ex.Message}", ex);
            }
        }

        private async Task<bool> CheckSftpFileExistsAsync(Uri sftpUri, NetworkCredential credentials, string remotePath)
        {
            try
            {
                using (var client = new SftpClient(sftpUri.Host, sftpUri.Port, credentials.UserName, credentials.Password))
                {
                    await Task.Run(() => client.Connect());

                    if (!client.IsConnected)
                        throw new Exception("Failed to connect to SFTP server");

                    return await Task.Run(() => client.Exists(remotePath));
                }
            }
            catch (Renci.SshNet.Common.SshAuthenticationException)
            {
                throw new Exception("SFTP authentication failed");
            }
            catch (Renci.SshNet.Common.SshConnectionException)
            {
                throw new Exception("SFTP connection failed");
            }
            catch (Exception ex)
            {
                throw new Exception($"SFTP check failed: {ex.Message}");
            }
        }

        private async Task UploadFileToFtpAsync(string filePath, Uri ftpUri, Action<FtpWebRequest> configureRequest)
        {
            var request = (FtpWebRequest)WebRequest.Create(ftpUri);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            configureRequest(request);

            // Stream directly from file to FTP without loading entire file into memory
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                                  bufferSize: 81920, // Optimal buffer size
                                                  useAsync: true))
            using (var requestStream = await request.GetRequestStreamAsync())
            {
                await fileStream.CopyToAsync(requestStream);
            }

            // Verify upload completed successfully
            using (var response = (FtpWebResponse)await request.GetResponseAsync())
            {
                if (response.StatusCode != FtpStatusCode.ClosingData)
                {
                    throw new Exception($"FTP upload failed with status: {response.StatusDescription}");
                }
            }
        }

        private async Task UploadFileToSftpAsync(string localPath, Uri sftpUri, NetworkCredential credentials, string remotePath)
        {
            try
            {
                using (var client = new SftpClient(sftpUri.Host, sftpUri.Port, credentials.UserName, credentials.Password))
                {
                    try
                    {
                        // Connect with timeout
                        client.OperationTimeout = TimeSpan.FromSeconds(30);
                        await Task.Run(() => client.Connect());

                        if (!client.IsConnected)
                            throw new Exception("Failed to connect to SFTP server");

                        // Ensure remote directory exists
                        var directoryPath = Path.GetDirectoryName(remotePath);
                        if (!string.IsNullOrEmpty(directoryPath))
                        {
                            await CreateRemoteDirectoryIfNotExists(client, directoryPath);
                        }

                        // Upload file with retry logic
                        using (var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true))
                        {
                            await Task.Run(() => client.UploadFile(fileStream, remotePath));
                            return;
                        }
                    }
                    catch (Renci.SshNet.Common.SftpPathNotFoundException ex)
                    {
                        throw new Exception($"Remote directory not found: {ex.Message}");
                    }
                    catch (Renci.SshNet.Common.SftpPermissionDeniedException ex)
                    {
                        throw new Exception($"Permission denied: {ex.Message}");
                    }
                    finally
                    {
                        if (client.IsConnected)
                        {
                            client.Disconnect();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"SFTP upload failed: {ex.Message}");
            }
        }

        private async Task CreateRemoteDirectoryIfNotExists(SftpClient client, string directoryPath)
        {
            try
            {
                // Check if directory exists
                if (!await Task.Run(() => client.Exists(directoryPath)))
                {
                    // Create directory recursively
                    await Task.Run(() => client.CreateDirectory(directoryPath));
                    await MessageLoggerHelper.LogMessageAsync($"Created remote directory: {directoryPath}",
                        _logger, _notificationService);
                }
            }
            catch (Renci.SshNet.Common.SftpPermissionDeniedException ex)
            {
                throw new Exception($"Cannot create directory - permission denied: {directoryPath}", ex);
            }
        }

        public override void Dispose()
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }
            base.Dispose();
        }
    }
}
