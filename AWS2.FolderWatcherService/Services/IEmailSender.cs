using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AWS2.FolderWatcherService.Services
{
    public interface IEmailSender
    {
        Task SendEmailAsync(string to, string subject, string body,string filePath,bool isBodyHtml);
    }
}
