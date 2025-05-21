using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AWS2.FolderWatcherService.Helpers;

namespace AWS2.FolderWatcherService.Models
{
    public class WatchedFolder
    {
        public string? Name { get; set; }
        public required string Path { get; set; }
        public string? ArchiveFilePath { get; set; }
        public required string ClientCode { get; set; }
        public bool IncludeSubdirectories { get; set; }
        public bool EnableMoving { get; set; } = true;
        public bool SendEmailAlerts { get; set; }
        public bool SendHttpAlerts { get; set; }
        public string? HttpAlertUrl { get; set; }
    }

    public class NotificationMessage
    {
        public string? EventType { get; set; }
        public string? FilePath { get; set; }
        public string? FolderName { get; set; }
        public DateTime Timestamp { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class ErrorEmailModel
    {
        public string? ApplicationName { get; set; } = ConstantMessagesHelper.applicationName;
        public string? Environment { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? ErrorType { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }
        public string? RequestUrl { get; set; }
        public string? CompanyName { get; set; } = ConstantMessagesHelper.companyName;
    }

    public class WarningEmailModal
    {
        public DateTime Timestamp { get; set; }
        public int? TotalFilesProcessed { get; set; } = 0;
        public int? FilesWithIssues { get; set; } = 0;
        //public int? CriticalIssues { get; set; } = 0;
        public IEnumerable<FileProcessingLogModal> FileProcessingLog { get; set; } = new List<FileProcessingLogModal>();
    }

    public class FileProcessingLogModal
    {
        public DateTime Timestamp { get; set; }
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? FileName { get; set; }
        public string? Details { get; set; }
    }
}
