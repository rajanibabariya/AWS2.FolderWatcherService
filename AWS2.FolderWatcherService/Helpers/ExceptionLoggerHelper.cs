using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AWS2.FolderWatcherService.Helpers
{
    public static class ExceptionLoggerHelper
    {
        // Directory to store the error log files
        private static readonly string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ErrorLogs");

        // Log file name template (based on current date)
        private static readonly string logFileName = "Log_{0}.txt"; // E.g., Log_2025-03-01.txt

        private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1); // Ensures only 1 thread at a time

        // Async method to log exceptions to a text file
        public static async Task LogExceptionAsync(Exception ex)
        {
            await _fileLock.WaitAsync(); // Async lock
            try
            {
                // Ensure the log directory exists, if not, create it
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                // Get current date to create a log file for each day (Log_YYYY-MM-DD.txt)
                string datePart = DateTime.Now.ToString("yyyy-MM-dd");
                string logFilePath = Path.Combine(logDirectory, string.Format(logFileName, datePart));

                // Prepare the exception details (message, stack trace, and timestamp)
                string exceptionDetails = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Exception: {ex.Message}{Environment.NewLine}" +
                                          $"Stack Trace: {ex.StackTrace}{Environment.NewLine}" +
                                          "----------------------------------------" + Environment.NewLine;

                // Append the exception details to the log file (creates new file if it doesn't exist)
                await File.AppendAllTextAsync(logFilePath, exceptionDetails);
            }
            catch (Exception logException)
            {
                // In case of an error while logging, you could log this to a different fallback location or handle accordingly
                Console.WriteLine($"Error logging exception: {logException.Message}");
            }
            finally
            {
                _fileLock.Release(); // Release lock
            }
        }
    }
}
