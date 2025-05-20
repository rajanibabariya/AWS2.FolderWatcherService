using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AWS2.FolderWatcherService.Helpers;
using AWS2.FolderWatcherService.Models;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace AWS2.FolderWatcherService.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ILogger<NotificationService> _logger;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;

        public NotificationService(ILogger<NotificationService> logger, IEmailSender emailSender, IConfiguration config)
        {
            _logger = logger;
            _emailSender = emailSender;
            _config = config;
        }

        public async Task SendEmailAlert(NotificationMessage message)
        {
            try
            {
                var subject = $"File {message.EventType} in {message.FolderName}";
                var body = $"File event detected:\n\n" +
                          $"Type: {message.EventType}\n" +
                          $"Path: {message.FilePath}\n" +
                          $"Time: {message.Timestamp:yyyy-MM-dd HH:mm:ss}" +
                          $"Error Message: {message.ErrorMessage}";

                await _emailSender.SendEmailAsync("rajani.babariya@azistaaerospace.com", subject, body, false);
                _logger.LogInformation($"Email alert sent for {message.EventType} event");
            }
            catch (Exception ex)
            {
                await ExceptionLoggerHelper.LogExceptionAsync(ex);
            }
        }

        public async Task SendHttpAlert(NotificationMessage message, string url)
        {
            try
            {
                using var httpClient = new HttpClient();
                var json = JsonSerializer.Serialize(message);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                _logger.LogInformation($"HTTP alert sent to {url}");
            }
            catch (Exception ex)
            {
                await ExceptionLoggerHelper.LogExceptionAsync(ex);
            }
        }

        public async Task SendErrorNotification(ErrorEmailModel notification)
        {
            try
            {
                var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EmailTemplates");
                if (Directory.Exists(templatePath))
                {
                    templatePath = Path.Combine(templatePath, "ErrorEmailTemplate.html");

                    var template = await File.ReadAllTextAsync(templatePath, Encoding.UTF8);

                    var body = template
                        .Replace("{{ApplicationName}}", notification.ApplicationName)
                        .Replace("{{Timestamp}}", notification.Timestamp.ToString("dd-MMM-yyyy HH:mm:ss"))
                        .Replace("{{ErrorType}}", notification.ErrorType)
                        .Replace("{{ErrorMessage}}", notification.ErrorMessage)
                        .Replace("{{StackTrace}}", notification.StackTrace)
                        .Replace("{{RequestUrl}}", notification.RequestUrl ?? "N/A")
                        .Replace("{{CompanyName}}", notification.CompanyName);

                    //var subject = $"[{_env.EnvironmentName}] {notification.ApplicationName} Error: {notification.ErrorType}";
                    var subject = $"{notification.ApplicationName} Error: {notification.ErrorType}";

                    var emailConfig = _config.GetSection("EmailSettings");
                    var to = emailConfig["ErrorNotificationRecipients"] ?? "dhaval.vora@azistaaerospace.com";

                    await _emailSender.SendEmailAsync(
                        to,
                        subject,
                        body,
                        true);

                    _logger.LogInformation("Error notification email sent");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send error notification email");
                throw;
            }
        }
    }
}
