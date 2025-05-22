using AWS2.FolderWatcherService.Models;
using AWS2.FolderWatcherService.Helpers;
using System.Net;
using System.Text;
using System.Text.Json;
using AWS2.FolderWatcherService.Services;

namespace AWS2.FolderWatcherService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _config;
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly string _hostName = Dns.GetHostName();
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly INotificationService _notificationService;

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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await MessageLoggerHelper.LogMessageAsync("Starting folder watcher service...", _logger, _notificationService);

                var foldersToWatch = _config.GetSection("WatchedFolders").Get<List<WatchedFolder>>();

                if (foldersToWatch is null || !foldersToWatch.Any())
                {
                    await MessageLoggerHelper.LogWarningAsync("No folders configured for watching", _logger, _notificationService);
                    return;
                }

                await InitializeWatchersAsync(foldersToWatch);

                while (!stoppingToken.IsCancellationRequested)
                {
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
                            if (t.IsFaulted)
                            {
                                // Log the error if needed
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


        private DateTime _lastStatsLogTime = DateTime.MinValue;
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
            foreach (var folder in folders)
            {
                try
                {
                    if (!Directory.Exists(folder.Path))
                    {
                        await MessageLoggerHelper.LogWarningAsync($"Directory {folder.Path} does not exist. Creating...", _logger, _notificationService);
                        Directory.CreateDirectory(folder.Path);
                    }

                    var watcher = new FileSystemWatcher(folder.Path)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                        IncludeSubdirectories = folder.IncludeSubdirectories,
                        EnableRaisingEvents = true
                    };

                    // Only subscribe to Created and Deleted events as per original code
                    //watcher.Created += async (sender, e) => await OnFileEvent(sender, e, "Created", folder);
                    watcher.Changed += async (sender, e) => await OnFileEvent(sender, e, "Changed", folder);
                    //watcher.Deleted += async (sender, e) => await OnFileEvent(sender, e, "Deleted", folder);
                    watcher.Error += async (sender, e) => await OnError(sender, e);

                    _watchers.Add(watcher);
                    await MessageLoggerHelper.LogMessageAsync($"Started watching folder: {folder.Path}", _logger, _notificationService);
                }
                catch (Exception ex)
                {
                    await ExceptionLoggerHelper.LogExceptionAsync(ex, _notificationService);
                }
            }
        }

        private async Task OnFileEvent(object sender, FileSystemEventArgs e, string eventType, WatchedFolder folderConfig)
        {
            try
            {
                IncrementFilesProcessed();
                await MessageLoggerHelper.LogMessageAsync($"File {eventType}: {e.FullPath}", _logger, _notificationService);

                if (eventType == "Deleted")
                {
                    // Handle deletion event differently if needed
                    return;
                }

                var processedFiles = new List<string>();

                if (!string.IsNullOrEmpty(e.Name)) processedFiles.Add(e.Name);

                if (!processedFiles.Any())
                {
                    IncrementFilesWithIssues(e.Name ?? "UnknownFile", "File Not Exist", folderConfig.Name ?? "UnknownName", folderConfig.Path);
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
                var fileContent = await ReadFileWithRetryAsync(e.FullPath);
                if (string.IsNullOrEmpty(fileContent))
                {
                    IncrementFilesWithIssues(e.Name ?? "UnknownFile", "File content is empty or null for file", folderConfig.Name ?? "UnknownName", folderConfig.Path);
                    await MessageLoggerHelper.LogWarningAsync($"File content is empty or null for file: {e.FullPath}", _logger, _notificationService);
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
                    IncrementFilesWithIssues(e.Name ?? "UnknownFile", response.Message ?? "Unknown error", folderConfig.Name ?? "UnknownName", folderConfig.Path);
                    await MessageLoggerHelper.LogWarningAsync($"{response.Message}: {e.Name}", _logger, _notificationService);
                    return;
                }

                if (folderConfig.EnableMoving)
                {
                    await MoveFile(e.FullPath, folderConfig);
                }

                await MessageLoggerHelper.LogMessageAsync($"Complete processing: {e.Name}", _logger, _notificationService);
            }
            catch (Exception ex)
            {
                await MessageLoggerHelper.LogErrorAsync(ex, $"Error processing file event: {e.FullPath}", _logger, _notificationService);
            }
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

        public async Task MoveFile(string sourcePath, WatchedFolder folder)
        {
            if (string.IsNullOrEmpty(folder.ArchiveFilePath))
            {
                await MessageLoggerHelper.LogWarningAsync("ArchiveFilePath is null. Cannot move file.", _logger, _notificationService);
                return;
            }

            if (!Directory.Exists(folder.ArchiveFilePath))
            {
                await MessageLoggerHelper.LogWarningAsync($"Archive directory {folder.ArchiveFilePath} does not exist. Creating...", _logger, _notificationService);
                Directory.CreateDirectory(folder.ArchiveFilePath);
            }

            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(folder.ArchiveFilePath, fileName);

            try
            {
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
