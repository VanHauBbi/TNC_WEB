using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;
using Newtonsoft.Json;

namespace WebBanHang.Controllers
{
    public class ChatController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<JsonResult> SendMessage(string userMessage)
        {
            if (string.IsNullOrEmpty(userMessage))
            {
                return Json(new { success = false, response = "Tin nhắn không được để trống." });
            }

            // Dùng Trim() để cắt khoảng trắng thừa, giúp so sánh từ khóa chính xác hơn
            string msgLower = userMessage.ToLower().Trim();

            // =========================================================================
            // 1. LỚP PHÒNG THỦ: ĐÁNH CHẶN LUỒNG GẶP ADMIN 
            // =========================================================================
            if (msgLower.Contains("gặp admin") || msgLower.Contains("nói chuyện với admin"))
            {
                string adminReply = "Hệ thống đã ghi nhận yêu cầu hỗ trợ. Hiện tại Admin đang Offline, bạn vui lòng để lại **Số điện thoại** hoặc **Email** ngay tại đây, Admin sẽ liên hệ lại với bạn trong thời gian sớm nhất nhé! 👇";
                return Json(new { success = true, reply = adminReply });
            }

            // =========================================================================
            // 2. LỚP PHÒNG THỦ: CÁC CÂU GIAO TIẾP CƠ BẢN VÀ CÂU CHUNG CHUNG
            // =========================================================================
            if (msgLower == "ok" || msgLower == "dạ" || msgLower == "vâng" || msgLower == "cảm ơn")
            {
                return Json(new { success = true, reply = "Dạ vâng, TNC Store luôn sẵn sàng. Bạn có cần tư vấn thêm về cấu hình hay linh kiện nào nữa không ạ? ✨" });
            }
            if (msgLower == "mua sản phẩm" || msgLower == "tư vấn" || msgLower == "cần mua")
            {
                return Json(new { success = true, reply = "Dạ, TNC Store đang có rất nhiều dòng sản phẩm. Bạn đang quan tâm đến danh mục nào ạ? 📦" });
            }
            if (msgLower.Contains("build pc"))
            {
                return Json(new { success = true, reply = "Tuyệt vời! Để cấu hình tối ưu nhất, bạn muốn Build PC phục vụ cho Gaming, Đồ họa hay Văn phòng cơ bản ạ? 🖥️" });
            }

            // =========================================================================
            // 3. LỚP PHÒNG THỦ: CHỐNG SPAM TIN NHẮN QUÁ NGẮN
            // =========================================================================
            if (msgLower.Length < 4)
            {
                return Json(new { success = true, reply = "Dạ bạn có thể nói rõ hơn một chút về nhu cầu của mình để hệ thống tư vấn chính xác nhất không ạ? 😊" });
            }

            // =========================================================================
            // 4. GỌI API GEMINI CHO CÁC YÊU CẦU CHUYÊN SÂU
            // =========================================================================
            //string apiKey = "AQ.Ab8RN6JOWSw4fZ96sthav_53SkmhIQQx17j3EGbCcdwSTeVH8w";
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            string systemPrompt = "Bạn là nhân viên tư vấn kỹ thuật thông minh của cửa hàng máy tính TNC Store. " +
                                  "Hãy trả lời ngắn gọn, lịch sự, tập trung tư vấn về linh kiện máy tính, cấu hình PC. Không dùng các ký tự markdown như dấu *.";

            var requestBody = new
            {
                contents = new[] {
                    new { parts = new[] { new { text = systemPrompt + "\nKhách hàng hỏi: " + userMessage } } }
                }
            };

            string jsonPayload = JsonConvert.SerializeObject(requestBody);

            try
            {
                // Bắt buộc sử dụng TLS 1.2 để vượt qua bảo mật của Google
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

                using (var client = new HttpClient())
                {
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(url, content);
                    string responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        dynamic jsonResponse = JsonConvert.DeserializeObject(responseString);
                        string aiReply = jsonResponse.candidates[0].content.parts[0].text;
                        return Json(new { success = true, reply = aiReply });
                    }
                    return Json(new { success = false, reply = "🤖 Xin lỗi, Trợ lý ảo TNC Store đang phục vụ quá nhiều khách hàng. Bạn vui lòng đợi khoảng 1 phút rồi thử lại nhé!" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, reply = "Lỗi kết nối C#: " + ex.Message });
            }
        }
    }
}