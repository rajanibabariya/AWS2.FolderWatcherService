using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
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

        public async Task SendEmailAsync(string to, string subject, string body, bool isBodyHtml)
        {
            var emailConfig = _config.GetSection("EmailSettings");

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("Application Error System", emailConfig["SenderEmail"]));
                message.To.AddRange(GetRecipients(to));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder();
                if (isBodyHtml)
                {
                    bodyBuilder.HtmlBody = body;
                }
                else
                {
                    bodyBuilder.TextBody = body;
                }
                message.Body = bodyBuilder.ToMessageBody();

                using var client = new MailKit.Net.Smtp.SmtpClient();

                // For debugging SSL issues (remove in production)
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;

                _logger.LogInformation($"Connecting to SMTP server: {emailConfig["SmtpServer"]}:{emailConfig.GetValue<int>("Port")}");

                await client.ConnectAsync(
                    emailConfig["SmtpServer"],
                    emailConfig.GetValue<int>("Port"),
                    SecureSocketOptions.StartTls);

                _logger.LogDebug("SMTP connection established. Authenticating...");

                await client.AuthenticateAsync(
                    emailConfig["Username"],
                    emailConfig["Password"]);

                _logger.LogDebug("Authentication successful. Sending email...");

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation($"Email successfully sent to {to}");
            }
            catch (AuthenticationException authEx)
            {
                _logger.LogError(authEx, "SMTP authentication failed. Check username/password.");
                throw new InvalidOperationException("Email authentication failed", authEx);
            }
            catch (SmtpCommandException smtpEx)
            {
                _logger.LogError(smtpEx, $"SMTP command error: {smtpEx.StatusCode} - {smtpEx.Message}");
                throw new InvalidOperationException("Email sending failed", smtpEx);
            }
            catch (SmtpProtocolException protoEx)
            {
                _logger.LogError(protoEx, $"SMTP protocol error: {protoEx.Message}");
                throw new InvalidOperationException("Email protocol error", protoEx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending email");
                throw;
            }
        }

        private InternetAddressList GetRecipients(string recipients)
        {
            var addressList = new InternetAddressList();
            foreach (var email in recipients.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    addressList.Add(MailboxAddress.Parse(email.Trim()));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Invalid email address format: {email}");
                }
            }

            if (!addressList.Any())
            {
                throw new InvalidOperationException("No valid recipients found");
            }

            return addressList;
        }
    }
}
