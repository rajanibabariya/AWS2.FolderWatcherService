using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AWS2.FolderWatcherService.Helpers
{
    
    public static class MessageLoggerHelper
    {
        private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1); // Ensures only 1 thread at a time
        private static readonly string _logFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FilesActivityLogs");

        public static async Task LogMessageAsync(string message, ILogger<Worker> _logger)
        {
            await _fileLock.WaitAsync(); // Async lock
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var logMessage = $"{timestamp} : {message}{Environment.NewLine}";
                var logFilePath = await GetLogFilePathAsync();

                await File.AppendAllTextAsync(logFilePath, logMessage);
                _logger.LogInformation(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write log message");
                await ExceptionLoggerHelper.LogExceptionAsync(ex);
            }
            finally
            {
                _fileLock.Release(); // Release lock
            }
        }

        public static async Task LogWarningAsync(string message, ILogger<Worker> _logger)
        {
            _logger.LogWarning(message);
            await LogMessageAsync($"WARNING: {message}",_logger);
        }

        public static async Task LogErrorAsync(Exception ex, string context, ILogger<Worker> _logger)
        {
            _logger.LogError(ex, context);
            await LogMessageAsync($"ERROR: {context} - {ex.Message}",_logger);
        }

        private static async Task<string> GetLogFilePathAsync()
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
                    await ExceptionLoggerHelper.LogExceptionAsync(ex);
                }
            }
            return Path.Combine(_logFolderPath, $"Log_{currentDate}.txt");
        }
    }
}
