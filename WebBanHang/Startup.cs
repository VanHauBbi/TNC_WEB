using Microsoft.Owin;
using Owin;

// Đường dẫn này chỉ định cho hệ thống biết đây là lớp khởi chạy OWIN
[assembly: OwinStartup(typeof(WebBanHang.Startup))]

namespace WebBanHang
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            // Kích hoạt và cấu hình đường ống SignalR cho toàn bộ ứng dụng
            app.MapSignalR();
        }
    }
}