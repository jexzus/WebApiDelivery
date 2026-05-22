using System.Net;
using System.Net.Mail;

namespace WebApiDelivery.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpClient _smtp;
        private readonly string _from;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration cfg, ILogger<SmtpEmailSender> logger)
        {
            _logger = logger;

            _from    = cfg["Email:From"]     ?? throw new ArgumentException("Email:From no configurado");
            var host = cfg["Email:SmtpHost"] ?? throw new ArgumentException("Email:SmtpHost no configurado");
            var portStr = cfg["Email:SmtpPort"] ?? throw new ArgumentException("Email:SmtpPort no configurado");
            var user = cfg["Email:User"]     ?? throw new ArgumentException("Email:User no configurado");
            var pass = cfg["Email:Pass"]     ?? throw new ArgumentException("Email:Pass no configurado");

            if (!int.TryParse(portStr, out int port))
                throw new ArgumentException("Email:SmtpPort debe ser un número válido");

            _smtp = new SmtpClient(host, port)
            {
                Credentials    = new NetworkCredential(user, pass),
                EnableSsl       = true,
                DeliveryMethod  = SmtpDeliveryMethod.Network,
                Timeout         = 30_000
            };

            _logger.LogInformation("SMTP configurado: {Host}:{Port} desde {From}", host, port, _from);
        }

        public async Task SendAsync(string to, string subject, string htmlBody)
        {
            try
            {
                _logger.LogInformation("Enviando email a {To} — {Subject}", to, subject);

                var msg = new MailMessage(_from, to, subject, htmlBody) { IsBodyHtml = true };
                await _smtp.SendMailAsync(msg);

                _logger.LogInformation("Email enviado a {To}", to);
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "Error SMTP al enviar a {To}: {Msg}", to, ex.Message);
                throw new InvalidOperationException($"No se pudo enviar el email: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al enviar email a {To}", to);
                throw;
            }
        }
    }
}
