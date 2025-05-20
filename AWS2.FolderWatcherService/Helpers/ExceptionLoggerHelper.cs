using System.Text;
using AWS2.FolderWatcherService.Services;

namespace AWS2.FolderWatcherService.Helpers
{
    public static class ExceptionLoggerHelper
    {
        private static readonly string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ErrorLogs");
        private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
        private static DateTime _lastLogDate = DateTime.MinValue;
        private static DateTime _lastEmailSentDate = DateTime.MinValue;

        public static async Task LogExceptionAsync(Exception ex, INotificationService notificationService)
        {
            await _fileLock.WaitAsync();
            try
            {
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                DateTime currentDate = DateTime.Now.Date;

                // If a new day has started and yesterday's log has not been sent
                if (currentDate > _lastEmailSentDate && notificationService != null)
                {
                    DateTime yesterday = currentDate.AddDays(-1);
                    string yesterdayLogPath = Path.Combine(logDirectory, $"Log_{yesterday:yyyy-MM-dd}.txt");

                    if (File.Exists(yesterdayLogPath))
                    {
                        await notificationService.SendErrorNotification(yesterdayLogPath);
                        _lastEmailSentDate = currentDate; // Mark email sent for the day
                    }
                }

                _lastLogDate = currentDate;
                string logFilePath = Path.Combine(logDirectory, $"Log_{currentDate:yyyy-MM-dd}.txt");

                var logBuilder = new StringBuilder();
                logBuilder.AppendLine("------------------------------------------------------------");
                logBuilder.AppendLine($"Timestamp      : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
                logBuilder.AppendLine($"Error Message  : {ex.Message}");
                logBuilder.AppendLine($"Stack Trace    : {ex.StackTrace}");

                if (ex.InnerException != null)
                    logBuilder.AppendLine($"Inner Exception: {ex.InnerException.Message}");

                if (!string.IsNullOrWhiteSpace(ex.Source))
                    logBuilder.AppendLine($"Source         : {ex.Source}");

                if (!string.IsNullOrWhiteSpace(ex.GetType().Name))
                    logBuilder.AppendLine($"Error Type     : {ex.GetType().Name}");

                logBuilder.AppendLine();

                await File.AppendAllTextAsync(logFilePath, logBuilder.ToString());
            }
            catch (Exception logException)
            {
                Console.WriteLine($"Error logging exception: {logException.Message}");
            }
            finally
            {
                _fileLock.Release();
            }
        }
    }
}
