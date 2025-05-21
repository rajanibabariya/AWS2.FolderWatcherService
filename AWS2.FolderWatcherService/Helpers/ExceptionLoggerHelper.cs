using System.Diagnostics;
using System.Text;
using AWS2.FolderWatcherService.Services;

namespace AWS2.FolderWatcherService.Helpers
{
    public static class ExceptionLoggerHelper
    {
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ErrorLogs");
        private static readonly SemaphoreSlim FileLock = new(1, 1);
        private static DateTime _currentDay = DateTime.Today;
        private static readonly string[] LogSeparator = new[] { Environment.NewLine + "------------------------------------------------------------" + Environment.NewLine };

        public static async Task LogExceptionAsync(Exception ex, INotificationService notificationService)
        {
            await FileLock.WaitAsync();
            try
            {
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }

                DateTime today = DateTime.Today;
                await HandleDayChangeNotification(today, notificationService);

                string logFilePath = Path.Combine(LogDirectory, $"Log_{today:yyyy-MM-dd}.txt");
                string logContent = BuildLogContent(ex);

                await File.AppendAllTextAsync(logFilePath, logContent);
            }
            catch (Exception logException)
            {
                // Consider adding a fallback logging mechanism here
                Debug.WriteLine($"Error logging exception: {logException.Message}");
            }
            finally
            {
                FileLock.Release();
            }
        }

        private static async Task HandleDayChangeNotification(DateTime today, INotificationService notificationService)
        {
            if (today > _currentDay && notificationService != null)
            {
                string yesterdayLogPath = Path.Combine(LogDirectory, $"Log_{_currentDay:yyyy-MM-dd}.txt");

                if (File.Exists(yesterdayLogPath))
                {
                    await notificationService.SendErrorNotification(yesterdayLogPath);
                    _currentDay = today;
                }
            }
        }

        private static string BuildLogContent(Exception ex)
        {
            var builder = new StringBuilder(512); // Pre-allocate reasonable capacity

            builder.Append(LogSeparator[0]); // Avoid string concatenation
            builder.AppendLine($"Timestamp      : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"Error Message  : {ex.Message}");
            builder.AppendLine($"Stack Trace    : {ex.StackTrace}");

            if (ex.InnerException != null)
            {
                builder.AppendLine($"Inner Exception: {ex.InnerException.Message}");
            }

            if (!string.IsNullOrWhiteSpace(ex.Source))
            {
                builder.AppendLine($"Source         : {ex.Source}");
            }

            string errorType = ex.GetType().Name;
            if (!string.IsNullOrWhiteSpace(errorType))
            {
                builder.AppendLine($"Error Type     : {errorType}");
            }

            builder.AppendLine();
            return builder.ToString();
        }


    }
}
