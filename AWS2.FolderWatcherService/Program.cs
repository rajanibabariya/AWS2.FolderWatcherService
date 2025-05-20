using System.Runtime.InteropServices;
using AWS2.FolderWatcherService;
using AWS2.FolderWatcherService.Services;


var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHttpClient();
// Windows Service configuration
//if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
//{
//    builder.UseWindowsService();
//}

// Linux systemd configuration
//if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
//{
//    builder.UseSystemd();
//}

//// Add platform-specific service hosting
if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "Folder Watcher Service";
    });
}
//else if (OperatingSystem.IsLinux())
//{
//    builder.Services.AddSystemd();
//}


// Add services
builder.Services.AddSingleton<IEmailSender, EmailSender>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddHostedService<Worker>();


var host = builder.Build();
host.Run();
