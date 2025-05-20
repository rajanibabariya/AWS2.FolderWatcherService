using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public string? ApplicationName { get; set; } = "AWS2 - Folder Watcher Service";
        public string? Environment { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? ErrorType { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }
        public string? RequestUrl { get; set; }
        public string? CompanyName { get; set; } = "Azista Industries Pvt. Ltd.";
    }
}
