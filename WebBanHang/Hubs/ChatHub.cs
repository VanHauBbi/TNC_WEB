using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using WebBanHang.Models;

namespace WebBanHang.Hubs
{
    public class ChatHub : Hub
    {
        // Khởi tạo DB Context để lưu tin nhắn trực tiếp khi chat
        private MyStoreEntities db = new MyStoreEntities();

        /// <summary>
        /// Hàm này giúp Khách hoặc Admin tham gia vào đúng "Phòng chat" dựa trên SessionID (Mã phiên hỗ trợ)
        /// </summary>
        public async Task JoinSession(int sessionId)
        {
            string groupName = "Session_" + sessionId;

            // Thêm kết nối hiện tại vào nhóm thời gian thực của phòng chat này
            await Groups.Add(Context.ConnectionId, groupName);
        }

        /// <summary>
        /// Hàm tiếp nhận tin nhắn, lưu vào Database và bắn cho người còn lại trong phòng chat
        /// </summary>
        /// <param name="sessionId">ID của phiên hỗ trợ (Phòng chat)</param>
        /// <param name="senderName">Tên người gửi</param>
        /// <param name="message">Nội dung tin nhắn</param>
        /// <param name="isAdmin">true nếu là Admin gửi, false nếu là Khách gửi</param>
        public void SendMessage(int sessionId, string senderName, string message, bool isAdmin)
        {
            string groupName = "Session_" + sessionId;

            try
            {
                // 1. LƯU TIN NHẮN VÀO DATABASE (Lưu lại lịch sử cuộc trò chuyện)
                var newMessage = new SupportMessage
                {
                    SessionID = sessionId,
                    IsAdmin = isAdmin,
                    MessageContent = message,
                    SendTime = DateTime.Now
                };
                db.SupportMessages.Add(newMessage);
                db.SaveChanges();

                // 2. PHÁT TIN NHẮN THỜI GIAN THỰC
                // Bắn tín hiệu đến TẤT CẢ các máy đang mở phòng chat này (gồm Khách và Admin phụ trách)
                // Hàm 'broadcastMessage' sẽ được viết bằng Javascript ở phía Giao diện Front-end
                Clients.Group(groupName).broadcastMessage(senderName, message, isAdmin, DateTime.Now.ToString("HH:mm"));
            }
            catch (Exception ex)
            {
                // Nếu lưu DB lỗi, gửi thông báo lỗi riêng cho người vừa bấm gửi (Caller) biết
                Clients.Caller.errorMessage("Không thể gửi tin nhắn do lỗi hệ thống!");
            }
        }
    }
}