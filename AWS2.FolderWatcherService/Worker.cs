using AWS2.FolderWatcherService.Models;
using AWS2.FolderWatcherService;
using AWS2.FolderWatcherService.Helpers;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Threading.Tasks;
using Org.BouncyCastle.Asn1.X509;
using AWS2.FolderWatcherService.Services;


namespace AWS2.FolderWatcherService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly string _hostName = Dns.GetHostName();
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly INotificationService _notificationService;


        public Worker(ILogger<Worker> logger, IConfiguration config, IHttpClientFactory httpClientFactory, INotificationService notificationService)
        {
            _logger = logger;
            _config = config;
            _httpClientFactory = httpClientFactory;
            _notificationService = notificationService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await MessageLoggerHelper.LogMessageAsync("Starting folder watcher service...", _logger);

                var foldersToWatch = _config.GetSection("WatchedFolders").Get<List<WatchedFolder>>();

                if (foldersToWatch == null || !foldersToWatch.Any())
                {
                    await MessageLoggerHelper.LogWarningAsync("No folders configured for watching", _logger);
                    return;
                }

                await InitializeWatchersAsync(foldersToWatch);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                await ExceptionLoggerHelper.LogExceptionAsync(ex);
            }
            finally
            {
                await MessageLoggerHelper.LogWarningAsync("Stopping folder watcher service...", _logger);
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
                        await MessageLoggerHelper.LogWarningAsync($"Directory {folder.Path} does not exist. Creating...", _logger);
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
                    //watcher.Deleted += async (sender, e) => await OnFileEvent(sender, e, "Deleted", folder);
                    watcher.Error += async (sender, e) => await OnError(sender, e);

                    _watchers.Add(watcher);
                    await MessageLoggerHelper.LogMessageAsync($"Started watching folder: {folder.Path}", _logger);
                }
                catch (Exception ex)
                {
                    await ExceptionLoggerHelper.LogExceptionAsync(ex);
                }
            }
        }

        private async Task OnFileEvent(object sender, FileSystemEventArgs e, string eventType, WatchedFolder folderConfig)
        {
            bool isError = false;
            try
            {
                // Prepare notification
                var message = new NotificationMessage
                {
                    EventType = eventType,
                    FilePath = e.FullPath,
                    FolderName = folderConfig.Name,
                    Timestamp = DateTime.Now
                };


                await MessageLoggerHelper.LogMessageAsync($"File {eventType}: {e.FullPath}", _logger);

                if (eventType == "Deleted")
                {
                    // Handle deletion event differently if needed
                    return;
                }

                var processedFiles = new List<string>();

                if (!string.IsNullOrEmpty(e.Name)) processedFiles.Add(e.Name);

                //if (processedFiles.Any())
                //{
                //    try
                //    {
                //        string apiUrlStoreFiles = $"{APIURLList.BaseURL}{APIURLList.ReceivesFileLogsAPI}"
                //                   .Replace("{clientCode}", folderConfig.ClientCode)
                //                   .Replace("{hostDetail}", _hostName);

                //        var storeResponse = await CallApiStoreFileLogsDataAsync(apiUrlStoreFiles, processedFiles);
                //        if (!storeResponse)
                //        {
                //            await MessageLoggerHelper.LogWarningAsync($"Error storing file logs", _logger);
                //        }
                //    }
                //    catch (Exception ex)
                //    {
                //        await MessageLoggerHelper.LogErrorAsync(ex, $"Exception storing file logs -> {ex.Message}", _logger);
                //        await ExceptionLoggerHelper.LogExceptionAsync(ex);
                //    }
                //}

                //// Process file content
                var fileContent = await ReadFileWithRetryAsync(e.FullPath);
                if (string.IsNullOrEmpty(fileContent))
                {
                    throw new Exception($"File content is empty or null for file: {e.FullPath}");
                }

                //// Prepare API URL
                //var apiUrl = $"{APIURLList.ReceivesStationEnvDataAPI}"
                //            .Replace("{BaseURL}", APIURLList.BaseURL)
                //            .Replace("{clientCode}", folderConfig.ClientCode)
                //            .Replace("{transMode}", "GPRS").Replace("{hostDetail}", _hostName);

                //// Call API
                //var response = await CallApiAsync(apiUrl, fileContent);
                //if (!response.IsSuccess)
                //{
                //    await MessageLoggerHelper.LogWarningAsync($"API response was not successful for file: {e.Name}", _logger);
                //    return;
                //}

                if (folderConfig.EnableMoving)
                {
                    await MoveFile(e.FullPath, folderConfig);
                }

                await MessageLoggerHelper.LogMessageAsync($"Complete processing: {e.Name}", _logger);
            }
            catch (Exception ex)
            {
                await MessageLoggerHelper.LogErrorAsync(ex, $"Error processing file event: {e.FullPath}", _logger);
                await ExceptionLoggerHelper.LogExceptionAsync(ex);

                await _notificationService.SendErrorNotification(new ErrorEmailModel
                {
                    ErrorType = ex.GetType().Name,
                    ErrorMessage = $"Failed to move file: {ex.Message}",
                    StackTrace = ex.StackTrace,
                    RequestUrl = ex.Source
                });
            }
            
        }

        private async Task<string?> ReadFileWithRetryAsync(string filePath, int maxAttempts = 5, int delayMs = 500)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    return await File.ReadAllTextAsync(filePath);
                }
                catch (IOException) when (attempt < maxAttempts - 1)
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
                    await MessageLoggerHelper.LogWarningAsync($"API call failed: {response.StatusCode} - {responseContent}", _logger);
                    return new ApiResultModal { IsSuccess = false };
                }

                try
                {
                    return JsonSerializer.Deserialize<ApiResultModal>(responseContent, _jsonOptions) ?? new ApiResultModal { IsSuccess = false };
                }
                catch (JsonException ex)
                {
                    await MessageLoggerHelper.LogErrorAsync(ex, "Failed to deserialize API response", _logger);
                    await ExceptionLoggerHelper.LogExceptionAsync(ex);
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
                        await MessageLoggerHelper.LogWarningAsync($"Error in file log API call -> {error}", _logger);
                        return false;
                    }

                    var contents = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var finalResponse = System.Text.Json.JsonSerializer.Deserialize<ApiResultModal>(contents);

                    if (finalResponse is null || !finalResponse.IsSuccess || finalResponse.StatusCode != 200)
                    {
                        await MessageLoggerHelper.LogWarningAsync("File log API response was not successful.", _logger);
                        return false;
                    }

                    await MessageLoggerHelper.LogMessageAsync("File log API response successful.", _logger);
                    return true;
                }
            }
            catch (Exception ex)
            {
                await MessageLoggerHelper.LogErrorAsync(ex, $"Error processing file -> {ex.Message}", _logger);
                await ExceptionLoggerHelper.LogExceptionAsync(ex);
                return false;
            }
        }

        public async Task MoveFile(string sourcePath, WatchedFolder folder)
        {
            if (string.IsNullOrEmpty(folder.ArchiveFilePath))
            {
                await MessageLoggerHelper.LogWarningAsync("ArchiveFilePath is null. Cannot move file.", _logger);
                return;
            }

            if (!Directory.Exists(folder.ArchiveFilePath))
            {
                await MessageLoggerHelper.LogWarningAsync($"Archive directory {folder.ArchiveFilePath} does not exist. Creating...", _logger);
                Directory.CreateDirectory(folder.ArchiveFilePath);
            }

            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(folder.ArchiveFilePath, fileName);
            //var uniqueDestPath = GetUniqueFileName(destPath);

            try
            {
                File.Move(sourcePath, destPath);
                await MessageLoggerHelper.LogMessageAsync($"Moved file from {sourcePath} to {destPath}", _logger);
            }
            catch (Exception ex)
            {
                await MessageLoggerHelper.LogErrorAsync(ex, $"Failed to move file from {sourcePath} to {destPath}", _logger);
            }
        }

        private string GetUniqueFileName(string originalPath)
        {
            if (!File.Exists(originalPath))
                return originalPath;

            var directory = Path.GetDirectoryName(originalPath);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            var extension = Path.GetExtension(originalPath);
            var counter = 1;

            string newPath;
            do
            {
                var newFileName = $"{fileNameWithoutExt}_{counter++}{extension}";
                newPath = Path.Combine(directory, newFileName);
            } while (File.Exists(newPath));

            return newPath;
        }

        private async Task OnError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            _logger.LogError(ex, "File system watcher error occurred");
            await ExceptionLoggerHelper.LogExceptionAsync(ex);
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
