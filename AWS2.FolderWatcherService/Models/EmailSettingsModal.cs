using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AWS2.FolderWatcherService.Models
{
    public class EmailSettingsModal
    {
        public string SenderName { get; set; } = "Application Error System";
        public required string SenderEmail { get; set; }
        public required string SmtpServer { get; set; }
        public int Port { get; set; } = 587; // Default SMTP port
        public required string Username { get; set; }
        public required string Password { get; set; }
        public bool EnableSsl { get; set; } = true;
        public int Timeout { get; set; } = 10000; // 10 seconds
        public required string ErrorDefaultRecipients { get; set; } // Comma-separated emails
    }
}
