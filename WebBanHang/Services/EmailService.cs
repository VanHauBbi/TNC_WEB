using System.Configuration;
using System.Net;
using System.Net.Mail;

namespace WebBanHang.Services
{
    public class EmailService
    {
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["SmtpHost"])
                                    && !string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["SmtpFrom"]);

        public void SendPasswordReset(string recipient, string resetUrl)
        {
            if (!IsConfigured) throw new ConfigurationErrorsException("SMTP chưa được cấu hình.");

            var host = ConfigurationManager.AppSettings["SmtpHost"];
            var port = int.TryParse(ConfigurationManager.AppSettings["SmtpPort"], out var parsedPort) ? parsedPort : 587;
            var username = ConfigurationManager.AppSettings["SmtpUsername"];
            var password = ConfigurationManager.AppSettings["SmtpPassword"];
            var from = ConfigurationManager.AppSettings["SmtpFrom"];

            using (var message = new MailMessage(from, recipient))
            using (var client = new SmtpClient(host, port))
            {
                message.Subject = "TNC Store - Đặt lại mật khẩu";
                message.Body = "Liên kết đặt lại mật khẩu có hiệu lực trong 30 phút:\r\n\r\n" + resetUrl
                             + "\r\n\r\nNếu bạn không yêu cầu, hãy bỏ qua email này.";
                message.IsBodyHtml = false;

                client.EnableSsl = true;
                if (!string.IsNullOrWhiteSpace(username))
                    client.Credentials = new NetworkCredential(username, password);
                client.Send(message);
            }
        }
    }
}
