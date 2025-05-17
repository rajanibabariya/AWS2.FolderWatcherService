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
        public required string ClientCode { get; set; }
        public bool IncludeSubdirectories { get; set; }
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
    }
}
