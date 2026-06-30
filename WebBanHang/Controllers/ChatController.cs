using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;
using WebBanHang.Models;
using System.Linq;
using System.Data.Entity;

namespace WebBanHang.Controllers
{
    // Class tạm để lưu trên RAM (Session)
    public class TempChatMessage
    {
        public string Sender { get; set; }
        public string Content { get; set; }
        public bool IsBot { get; set; }
        public DateTime Time { get; set; }
    }

    public class ChatController : Controller
    {
        private MyStoreEntities db = new MyStoreEntities();

        public ActionResult Index()
        {
            return View();
        }

        // HÀM HỖ TRỢ: Thêm tin nhắn vào RAM
        private void AddToTempHistory(string sender, string content, bool isBot)
        {
            var history = Session["TempChatHistory"] as List<TempChatMessage> ?? new List<TempChatMessage>();
            history.Add(new TempChatMessage { Sender = sender, Content = content, IsBot = isBot, Time = DateTime.Now });
            Session["TempChatHistory"] = history;
        }

        [HttpGet]
        public JsonResult GetActiveChatData()
        {
            // 1. MẤU CHỐT: Xóa sạch bộ nhớ tạm AI mỗi khi người dùng Refresh (F5) trang
            Session["TempChatHistory"] = null;

            if (Session["CustomerID"] != null)
            {
                int customerId = (int)Session["CustomerID"];

                // 2. Kiểm tra xem CÓ PHIÊN ADMIN NÀO ĐANG MỞ KHÔNG (Status != 2)
                var sqlSession = db.SupportSessions
                    .Include(s => s.SupportMessages)
                    .FirstOrDefault(s => s.CustomerID == customerId && s.Status != 2);

                if (sqlSession != null)
                {
                    // Nếu có phiên Admin, kéo lịch sử từ SQL về
                    var msgs = sqlSession.SupportMessages.OrderBy(m => m.SendTime).Select(m => new {
                        Sender = m.IsAdmin ? "Admin" : (m.MessageContent.StartsWith("AI: ") ? "AI" : "Bạn"),
                        Content = m.MessageContent.StartsWith("AI: ") ? m.MessageContent.Substring(4) : m.MessageContent
                    }).ToList();

                    return Json(new { hasSession = true, isLiveChat = true, sessionId = sqlSession.SessionID, messages = msgs, pendingConfirm = (sqlSession.Status == 3) }, JsonRequestBehavior.AllowGet);
                }
            }

            // Trả về false để UI biết và tạo mới khung chat AI tinh tươm
            return Json(new { hasSession = false, isLiveChat = false }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public async Task<JsonResult> SendMessage(string userMessage)
        {
            if (string.IsNullOrEmpty(userMessage)) return Json(new { success = false });

            string msgLower = userMessage.ToLower().Trim();
            int? customerId = Session["CustomerID"] as int?;

            // BƯỚC 1: KIỂM TRA ĐANG CHAT VỚI ADMIN THÌ LƯU SQL LUÔN
            if (customerId != null)
            {
                var activeSession = db.SupportSessions.FirstOrDefault(s => s.CustomerID == customerId && s.Status != 2);
                if (activeSession != null)
                {
                    // Đang chat với Admin -> Lưu thẳng vào SQL, không dùng RAM
                    db.SupportMessages.Add(new SupportMessage
                    {
                        SessionID = activeSession.SessionID,
                        IsAdmin = false,
                        MessageContent = userMessage,
                        SendTime = DateTime.Now
                    });
                    db.SaveChanges();

                    // Báo cho Client biết đang ở mode LiveChat để SignalR xử lý
                    return Json(new { success = true, isLiveChat = true, sessionId = activeSession.SessionID });
                }
            }

            // BƯỚC 2: NẾU ĐANG CHAT VỚI AI -> LƯU TẠM VÀO RAM
            AddToTempHistory("Bạn", userMessage, false);

            // BƯỚC 3: KHI YÊU CẦU "GẶP ADMIN" -> TẠO PHIÊN & ĐỔ DỮ LIỆU TỪ RAM VÀO SQL
            if (msgLower.Contains("gặp admin") || msgLower.Contains("nói chuyện với admin"))
            {
                if (customerId == null)
                    return Json(new { success = false, reply = "Dạ, vui lòng [Đăng nhập] để gặp Admin nhé!" });

                // Tạo phiên SQL MỚI
                var newSession = new SupportSession { CustomerID = customerId.Value, StartTime = DateTime.Now, Status = 0 };
                db.SupportSessions.Add(newSession);
                db.SaveChanges();

                // ĐỔ TOÀN BỘ LỊCH SỬ TỪ RAM SANG SQL
                var tempHistory = Session["TempChatHistory"] as List<TempChatMessage>;
                if (tempHistory != null)
                {
                    foreach (var msg in tempHistory)
                    {
                        string content = msg.IsBot ? "AI: " + msg.Content : msg.Content;
                        db.SupportMessages.Add(new SupportMessage
                        {
                            SessionID = newSession.SessionID,
                            IsAdmin = false,
                            MessageContent = content,
                            SendTime = msg.Time
                        });
                    }
                    db.SaveChanges();
                    Session["TempChatHistory"] = null; // Xóa RAM ngay sau khi đã chép sang SQL
                }

                string adminReply = "🚀 Hệ thống đã chuyển lịch sử chat đến Admin. Bạn vui lòng chờ giây lát nhé!";
                db.SupportMessages.Add(new SupportMessage { SessionID = newSession.SessionID, IsAdmin = false, MessageContent = "AI: " + adminReply, SendTime = DateTime.Now });
                db.SaveChanges();

                return Json(new { success = true, isLiveChat = true, sessionId = newSession.SessionID, reply = adminReply });
            }

            // BƯỚC 4: GỌI API GEMINI NẾU LÀ CHAT BÌNH THƯỜNG
            string aiReply = await GetGeminiResponse(userMessage);

            // Lưu phản hồi của AI vào RAM
            AddToTempHistory("AI", aiReply, true);

            return Json(new { success = true, isLiveChat = false, reply = aiReply });
        }

        // Gọi API Gemini 
        private async Task<string> GetGeminiResponse(string userMessage)
        {
            string apiKey = "AQ.Ab8RN6IPL1Q4E-DzkdOAr-W7r1XC44MB11bEVkEy0XbB42Ry-w";
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var requestBody = new { contents = new[] { new { parts = new[] { new { text = "Trả lời ngắn gọn: " + userMessage } } } } };
            string jsonPayload = JsonConvert.SerializeObject(requestBody);
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                using (var client = new HttpClient())
                {
                    var response = await client.PostAsync(url, new StringContent(jsonPayload, Encoding.UTF8, "application/json"));
                    if (response.IsSuccessStatusCode)
                    {
                        dynamic jsonResponse = JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync());
                        return jsonResponse.candidates[0].content.parts[0].text;
                    }
                }
            }
            catch { }
            return "Xin lỗi, hệ thống của chúng tôi đang bận xử lý. Vui lòng thử lại sau ít phút.";
        }

        // XÓA PHIÊN AI (Dùng cho Timeout 3 phút)
        [HttpPost]
        public JsonResult ClearAITempSession()
        {
            Session["TempChatHistory"] = null;
            return Json(new { success = true });
        }

        // XÁC NHẬN TỪ USER (Giải quyết xong chưa?)
        [HttpPost]
        public ActionResult ConfirmResolution(int sessionId, bool isResolved)
        {
            var session = db.SupportSessions.Find(sessionId);
            if (session != null)
            {
                if (isResolved)
                {
                    // Khách bấm RỒI -> Đóng phiên hoàn toàn
                    session.Status = 2;
                }
                else
                {
                    // Khách bấm CHƯA -> Mở lại phiên để chat tiếp
                    session.Status = 1;
                }

                db.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }

        // LẤY DANH SÁCH CÁC PHIÊN ĐÃ ĐÓNG (LỊCH SỬ)
        [HttpGet]
        public ActionResult GetUserHistory()
        {
            if (Session["CustomerID"] == null)
                return Content("<div class='text-center text-danger my-3'>Vui lòng đăng nhập để xem lịch sử.</div>");

            int customerId = (int)Session["CustomerID"];

            // Lấy các phiên ĐÃ ĐÓNG (Status = 2)
            var histories = db.SupportSessions
                              .Where(s => s.CustomerID == customerId && s.Status == 2)
                              .OrderByDescending(s => s.StartTime)
                              .ToList();

            if (!histories.Any())
                return Content("<div class='text-center text-muted my-3'>Bạn chưa có lịch sử hỗ trợ nào.</div>");

            string html = "";
            foreach (var item in histories)
            {
                html += $@"<div class='card shadow-sm mb-2 border-0' style='cursor:pointer;' onclick='viewHistoryDetail({item.SessionID})'>
                      <div class='card-body p-2' style='font-size: 0.85rem;'>
                          <strong class='text-primary'>Phòng #{item.SessionID}</strong><br>
                          <span class='text-muted'><i class='fa-regular fa-clock'></i> {item.StartTime.ToString("dd/MM/yyyy HH:mm")}</span>
                      </div>
                   </div>";
            }

            // Nút đóng Panel
            html += "<button class='btn btn-outline-secondary btn-sm w-100 mt-2' onclick='toggleHistoryPanel()'>Quay lại Chat</button>";

            return Content(html);
        }

        // LẤY CHI TIẾT LỊCH SỬ CỦA 1 PHIÊN 
        [HttpGet]
        public JsonResult GetSessionMessages(int sessionId)
        {
            // Tìm phiên chat và toàn bộ tin nhắn của nó
            var session = db.SupportSessions.Include(s => s.SupportMessages).FirstOrDefault(s => s.SessionID == sessionId);

            if (session != null)
            {
                var msgs = session.SupportMessages.OrderBy(m => m.SendTime).Select(m => new {
                    Sender = m.IsAdmin ? "Admin" : (m.MessageContent.StartsWith("AI: ") ? "AI" : "Bạn"),
                    Content = m.MessageContent.StartsWith("AI: ") ? m.MessageContent.Substring(4) : m.MessageContent,
                    Time = m.SendTime.ToString("HH:mm")
                }).ToList();

                return Json(new { success = true, messages = msgs }, JsonRequestBehavior.AllowGet);
            }
            return Json(new { success = false }, JsonRequestBehavior.AllowGet);
        }
    }
}