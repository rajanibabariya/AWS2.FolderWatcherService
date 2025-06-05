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
        public int? Id { get; set; }
        public string? Name { get; set; }
        public string? FolderPath { get; set; }
        public string? ArchiveFolderPath { get; set; }
        public string? ClientCode { get; set; }
        public bool IncludeSubDirectories { get; set; } = false;
        public bool EnableMovingForArchiveFolder { get; set; } = false;
        public string? CopyFileFtpServerName { get; set; }
        public string? CopyFileFtpUsername { get; set; }
        public string? CopyFileFtpPassword { get; set; }
        public string? CopyFileFtpAccessDirectory { get; set; }
        public bool CopyFileSecure { get; set; } = false;
        public int CopyFileFtpPort { get; set; }
        public bool CopyFileForOtherServer { get; set; } = false;

        //public string? Name { get; set; }
        //public required string FolderPath { get; set; }
        //public string? ArchiveFolderPath { get; set; }
        //public required string ClientCode { get; set; }
        //public bool IncludeSubDirectories { get; set; }
        //public bool EnableMovingForArchiveFolder { get; set; } = true;
        //public bool CopyFileForOtherServer { get; set; } = false;
        //public string? CopyFileFTPServerName { get; set; }
        //public string? CopyFileFTPUsername { get; set; }
        //public string? CopyFileFTPPassword { get; set; }
        //public string? CopyFileFTPAccessDirectory { get; set; }
        //public bool? CopyFileSecure { get; set; }
        //public int? CopyFileFtpPort { get; set; }    
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
