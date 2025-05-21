using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AWS2.FolderWatcherService.Services;

namespace AWS2.FolderWatcherService.Helpers
{
    
    public static class MessageLoggerHelper
    {
        private static readonly SemaphoreSlim _fileLock = new(1, 1);
        private static readonly string _logFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FilesActivityLogs");
        private static DateTime _lastLogDate = DateTime.MinValue;
        private static string _currentLogFilePath = string.Empty;
        private static DateTime _lastEmailSentDate = DateTime.MinValue;

        public static async Task LogMessageAsync(string message, ILogger<Worker> logger, INotificationService notificationService)
        {
            await LogInternalAsync(message, logger, notificationService);
        }

        public static async Task LogWarningAsync(string message, ILogger<Worker> logger, INotificationService notificationService)
        {
            await LogInternalAsync($"WARNING: {message}", logger, notificationService);
            logger.LogWarning(message);
        }

        public static async Task LogErrorAsync(Exception ex, string context, ILogger<Worker> logger, INotificationService notificationService)
        {
            var errorMessage = $"ERROR: {context} - {ex.Message}";
            await LogInternalAsync(errorMessage, logger, notificationService);
            logger.LogError(ex, context);

            // Log full exception details separately
            await ExceptionLoggerHelper.LogExceptionAsync(ex, notificationService);
        }


        private static async Task LogInternalAsync(string message, ILogger<Worker> logger, INotificationService notificationService)
        {
            await _fileLock.WaitAsync();
            try
            {
                EnsureLogDirectoryExists(notificationService);
                await UpdateLogFilePathIfNeeded(notificationService);

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var logMessage = $"{timestamp} : {message}{Environment.NewLine}";

                await File.AppendAllTextAsync(_currentLogFilePath, logMessage);
                logger.LogInformation(message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to write log message");
                await ExceptionLoggerHelper.LogExceptionAsync(ex, notificationService);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private static void EnsureLogDirectoryExists(INotificationService notificationService)
        {
            if (!Directory.Exists(_logFolderPath))
            {
                try
                {
                    Directory.CreateDirectory(_logFolderPath);
                }
                catch (Exception ex)
                {
                    // Fire-and-forget for directory creation errors
                    _ = ExceptionLoggerHelper.LogExceptionAsync(ex, notificationService);
                }
            }
        }

        private static async Task UpdateLogFilePathIfNeeded(INotificationService notificationService)
        {
            var currentDate = DateTime.Now.Date;

            if (currentDate != _lastLogDate)
            {
                _currentLogFilePath = Path.Combine(_logFolderPath, $"Log_{currentDate:yyyyMMdd}.txt");
                _lastLogDate = currentDate;
            }
        }        
    }
}
