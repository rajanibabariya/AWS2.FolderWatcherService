using AWS2.FolderWatcherService.Models;
using AWS2.FolderWatcherService;
using AWS2.FolderWatcherService.Helpers;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Net.Http;


namespace AWS2.FolderWatcherService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly string _hostName = Dns.GetHostName();
        private readonly string _logFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        public Worker(ILogger<Worker> logger, IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                LogMessage("Starting folder watcher service...");

                var foldersToWatch = _config.GetSection("WatchedFolders").Get<List<WatchedFolder>>();

                if (foldersToWatch == null || !foldersToWatch.Any())
                {
                    LogWarning("No folders configured for watching");
                    return;
                }

                InitializeWatchers(foldersToWatch);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex);
            }
            finally
            {
                LogMessage("Stopping folder watcher service...");
            }
        }

        private void InitializeWatchers(List<WatchedFolder> folders)
        {
            foreach (var folder in folders)
            {
                try
                {
                    if (!Directory.Exists(folder.Path))
                    {
                        LogWarning($"Directory {folder.Path} does not exist. Creating...");
                        Directory.CreateDirectory(folder.Path);
                    }

                    var watcher = new FileSystemWatcher(folder.Path)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                        IncludeSubdirectories = folder.IncludeSubdirectories,
                        EnableRaisingEvents = true
                    };

                    // Only subscribe to Created and Deleted events as per original code
                    watcher.Created += async (sender, e) => await OnFileEvent(sender, e, "Created", folder);
                    watcher.Deleted += async (sender, e) => await OnFileEvent(sender, e, "Deleted", folder);

                    _watchers.Add(watcher);
                    LogMessage($"Started watching folder: {folder.Path}");
                }
                catch (Exception ex)
                {
                    ExceptionLogger.LogException(ex);
                }
            }
        }

        private async Task OnFileEvent(object sender, FileSystemEventArgs e, string eventType, WatchedFolder folderConfig)
        {
            try
            {
                LogMessage($"File {eventType}: {e.FullPath}");

                if (eventType == "Deleted")
                {
                    // Handle deletion event differently if needed
                    return;
                }

                // Process file content
                var fileContent = await ReadFileWithRetryAsync(e.FullPath);
                if (fileContent == null) return;

                // Prepare API URL
                var apiUrl = $"{APIURLList.ReceivesStationEnvDataAPI}"
                            .Replace("{BaseURL}",APIURLList.BaseURL)
                            .Replace("{clientCode}", folderConfig.ClientCode)
                            .Replace("{transMode}", "GPRS").Replace("{hostDetail}", _hostName);

                // Call API
                var response = await CallApiAsync(apiUrl, fileContent);
                if (!response.IsSuccess)
                {
                    LogWarning($"API response was not successful for file: {e.Name}");
                    return;
                }

                LogMessage($"Complete processing: {e.Name}");
            }
            catch (Exception ex)
            {
                LogError(ex, $"Error processing file event: {e.FullPath}");
                ExceptionLogger.LogException(ex);
            }
        }

        private async Task<string?> ReadFileWithRetryAsync(string filePath, int maxRetries = 3, int delayMs = 500)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await File.ReadAllTextAsync(filePath);
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    await Task.Delay(delayMs);
                }
            }
            return null;
        }

        private async Task<ApiResultModal> CallApiAsync(string apiUrl, string content)
        {
            //using var httpClient = _httpClientFactory.CreateClient();
            //using var requestContent = new StringContent(content, Encoding.UTF8, "text/plain");

            using (HttpClient httpClient = new HttpClient())
            {

                var requestContent = new StringContent(content, Encoding.UTF8, "text/plain");
                var response = await httpClient.PostAsync(apiUrl, requestContent).ConfigureAwait(false);

                //var response = await httpClient.PostAsync(apiUrl, requestContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    LogWarning($"API call failed: {response.StatusCode} - {responseContent}");
                    return new ApiResultModal { IsSuccess = false };
                }

                try
                {
                    return JsonSerializer.Deserialize<ApiResultModal>(responseContent, _jsonOptions)
                        ?? new ApiResultModal { IsSuccess = false };
                }
                catch (JsonException ex)
                {
                    LogError(ex, "Failed to deserialize API response");
                    ExceptionLogger.LogException(ex);
                    return new ApiResultModal { IsSuccess = false };
                }
            }
        }

        private void LogMessage(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var logMessage = $"{timestamp} : {message}{Environment.NewLine}";
                var logFilePath = GetLogFilePath();

                File.AppendAllText(logFilePath, logMessage);
                _logger.LogInformation(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write log message");
                ExceptionLogger.LogException(ex);
            }
        }

        private void LogWarning(string message)
        {
            _logger.LogWarning(message);
            LogMessage($"WARNING: {message}");
        }

        private void LogError(Exception ex, string context)
        {
            _logger.LogError(ex, context);
            LogMessage($"ERROR: {context} - {ex.Message}");
        }

        private string GetLogFilePath()
        {
            var currentDate = DateTime.Now.ToString("yyyyMMdd");
            if (!Directory.Exists(_logFolderPath))
            {
                try
                {
                    Directory.CreateDirectory(_logFolderPath);
                }
                catch (Exception ex)
                {
                    ExceptionLogger.LogException(ex);
                }
            }
            return Path.Combine(_logFolderPath, $"Log_{currentDate}.txt");
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
