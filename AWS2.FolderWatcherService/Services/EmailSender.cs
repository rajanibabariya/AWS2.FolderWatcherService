using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MimeKit;

namespace AWS2.FolderWatcherService.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration config, ILogger<EmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string to, string subject, string body)
        {
            try
            {
                var emailConfig = _config.GetSection("EmailSettings");

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("Folder Watcher", emailConfig["FromEmail"]));
                message.To.Add(new MailboxAddress("Admin", to));
                message.Subject = subject;

                message.Body = new TextPart("plain") { Text = body };

                using var client = new MailKit.Net.Smtp.SmtpClient();
                await client.ConnectAsync(
                    emailConfig["SmtpServer"],
                    emailConfig.GetValue<int>("Port"),
                    MailKit.Security.SecureSocketOptions.StartTls);

                await client.AuthenticateAsync(emailConfig["Username"], emailConfig["Password"]);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation($"Email sent to {to} with subject: {subject}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email");
                throw;
            }
        }
    }
}
