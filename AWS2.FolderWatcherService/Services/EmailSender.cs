using AWS2.FolderWatcherService.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AWS2.FolderWatcherService.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailSender> _logger;
        private readonly EmailSettingsModal _emailSettings;

        public EmailSender(IConfiguration config, ILogger<EmailSender> logger)
        {
            _config = config;
            _logger = logger;

            var emailSettings = _config.GetSection("EmailSettings").Get<EmailSettingsModal>();
            if (emailSettings == null)
            {
                throw new InvalidOperationException("EmailSettings configuration section is missing or invalid.");
            }

            _emailSettings = new EmailSettingsModal
            {
                SenderEmail = emailSettings.SenderEmail,
                SmtpServer = emailSettings.SmtpServer,
                Username = emailSettings.Username,
                Password = emailSettings.Password,
                ErrorDefaultRecipients = emailSettings.ErrorDefaultRecipients,
                SenderName = emailSettings.SenderName,
                Port = emailSettings.Port,
                EnableSsl = emailSettings.EnableSsl,
                Timeout = emailSettings.Timeout
            };
        }

        public async Task SendEmailAsync(string to, string subject, string body, string filePath, bool isBodyHtml)
        {
            try
            {
                using var client = new SmtpClient();
                var message = CreateEmailMessage(to, subject, body, filePath, isBodyHtml);

                await ConnectAndSendAsync(client, message);

                _logger.LogInformation("Email successfully sent to {Recipient}", to);
            }
            catch (Exception ex) when (ex is AuthenticationException or SmtpCommandException or SmtpProtocolException)
            {
                _logger.LogError(ex, "Email sending failed: {Message}", ex.Message);
                throw;
            }
        }

        private MimeMessage CreateEmailMessage(string to, string subject, string body, string filePath, bool isBodyHtml)
        {
            var message = new MimeMessage
            {
                Subject = subject,
                From = { new MailboxAddress("Application Error System", _emailSettings.SenderEmail) }
            };

            message.To.AddRange(ParseRecipients(to));

            var builder = new BodyBuilder
            {
                TextBody = isBodyHtml ? null : body,
                HtmlBody = isBodyHtml ? body : null
            };

            if (!string.IsNullOrEmpty(filePath))
            {
                builder.Attachments.Add(filePath);
            }

            message.Body = builder.ToMessageBody();
            return message;
        }

        private InternetAddressList ParseRecipients(string recipients)
        {
            var addressList = new InternetAddressList();
            foreach (var email in recipients.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (MailboxAddress.TryParse(email.Trim(), out var mailbox))
                {
                    addressList.Add(mailbox);
                }
                else
                {
                    _logger.LogWarning("Invalid email address format: {Email}", email);
                }
            }

            return addressList.Any()
                ? addressList
                : throw new InvalidOperationException("No valid recipients found");
        }
        private async Task ConnectAndSendAsync(SmtpClient client, MimeMessage message)
        {
            _logger.LogInformation("Connecting to SMTP server: {Server}:{Port}",
                _emailSettings.SmtpServer, _emailSettings.Port);

            await client.ConnectAsync(
                _emailSettings.SmtpServer,
                _emailSettings.Port,
                SecureSocketOptions.StartTls);

            _logger.LogDebug("SMTP connection established. Authenticating...");

            await client.AuthenticateAsync(
                _emailSettings.Username,
                _emailSettings.Password);

            _logger.LogDebug("Authentication successful. Sending email...");
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
