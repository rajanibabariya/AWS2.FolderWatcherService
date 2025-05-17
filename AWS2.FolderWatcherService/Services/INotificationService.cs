using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AWS2.FolderWatcherService.Models;

namespace AWS2.FolderWatcherService.Services
{
    public interface INotificationService
    {
        Task SendEmailAlert(NotificationMessage message);
        Task SendHttpAlert(NotificationMessage message, string url);
    }
}
