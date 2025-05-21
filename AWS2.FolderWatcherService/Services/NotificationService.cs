using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
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
        private readonly string _baseDir = AppDomain.CurrentDomain.BaseDirectory;

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

                await _emailSender.SendEmailAsync("rajani.babariya@azistaaerospace.com", subject, body, string.Empty, false);
                _logger.LogInformation($"Email alert sent for {message.EventType} event");
            }
            catch (Exception ex)
            {
                await ExceptionLoggerHelper.LogExceptionAsync(ex, this);
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
                await ExceptionLoggerHelper.LogExceptionAsync(ex, this);
            }
        }

        public async Task SendErrorNotification(string filePath)
        {
            try
            {
                // Combine paths more efficiently
                var templatePath = Path.Combine(_baseDir, "EmailTemplates", "ErrorEmailTemplate.html");

                if (!File.Exists(templatePath))
                {
                    _logger.LogError("Email template not found at {TemplatePath}", templatePath);
                    throw new FileNotFoundException("Email template not found", templatePath);
                }

                var template = await File.ReadAllTextAsync(templatePath, Encoding.UTF8);
                var body = template
                    .Replace("{{companyName}}", ConstantMessagesHelper.companyName ?? "Azista Industries Pvt. Ltd.");

                var subject = $"AWS | Daily Error Log - {Path.GetFileNameWithoutExtension(Path.GetFileName(filePath))}";

                var emailConfig = _config.GetSection("EmailSettings");
                var to = emailConfig["ErrorDefaultRecipients"] ?? "dhaval.vora@azistaaerospace.com";

                await _emailSender.SendEmailAsync(to, subject, body, filePath, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send error notification email");
                throw;
            }
        }

        public async Task SendWarningNotification(WarningEmailModal warningEmail)
        {
            try
            {
                // Combine paths more efficiently
                var templatePath = Path.Combine(_baseDir, "EmailTemplates", "WarningEmailTemplates.html");

                if (!File.Exists(templatePath))
                {
                    _logger.LogError("Email template not found at {TemplatePath}", templatePath);
                    throw new FileNotFoundException("Email template not found", templatePath);
                }


                // Generate CSV content first to validate before creating file
                var csvBuilder = new StringBuilder();
                csvBuilder.AppendLine("Timestamp,Name,Path,FileName,Details");

                // Pre-order the collection to avoid multiple iterations
                var orderedWarnings = warningEmail.FileProcessingLog.OrderBy(w => w.Timestamp).ToList();

                foreach (var warning in orderedWarnings)
                {
                    // Use string interpolation with escaping
                    csvBuilder.AppendLine($"\"{warning.Timestamp:dd-MMM-yyyy HH:mm:ss}\",\"{warning.Name}\",\"{warning.Path}\",\"{warning.FileName?.Replace("\"", "\"\"")}\",\"{warning.Details?.Replace("\"", "\"\"")}\"");
                }


                // Create temp directory if needed
                var csvDirectory = Path.Combine(_baseDir, "TempCsv");
                Directory.CreateDirectory(csvDirectory);

                var csvFileName = $"WarningLog_{warningEmail.Timestamp:yyyyMMdd_HHmmss}.csv";
                var csvFilePath = Path.Combine(csvDirectory, csvFileName);

                // Write CSV file asynchronously
                await File.WriteAllTextAsync(csvFilePath, csvBuilder.ToString(), Encoding.UTF8);


                // Read template and replace placeholders in one pass
                var template = await File.ReadAllTextAsync(templatePath, Encoding.UTF8);
                var body = template
                    .Replace("{{totalFilesProcessed}}", warningEmail.TotalFilesProcessed.ToString())
                    .Replace("{{filesWithIssues}}", warningEmail.FilesWithIssues.ToString())
                    .Replace("{{dateTime}}", warningEmail.Timestamp.ToString("dd-MMM-yyyy"))
                    .Replace("{{companyName}}", ConstantMessagesHelper.companyName ?? "Azista Industries Pvt. Ltd.");

                var subject = $"AWS | Daily File Processing Warning Alert - {warningEmail.Timestamp:yyyy-MM-dd}";

                // Get email settings once
                var emailConfig = _config.GetSection("EmailSettings");
                var to = emailConfig["WarningDefaultRecipients"] ?? "dhaval.vora@azistaerospace.com";

                // Send email with attachment
                await _emailSender.SendEmailAsync(to, subject, body, csvFilePath, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send error notification email");
                throw;
            }
        }
    }
}
