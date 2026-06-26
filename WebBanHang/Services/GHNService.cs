using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebBanHang.Services
{
    public class GHNService
    {
        // TO-DO: Điền lại API Token và Shop ID của bạn
        private readonly string _apiToken = "621b5048-7064-11f1-a973-aee5264794df";
        private readonly string _shopId = "200862";

        // ĐÃ SỬA: Bỏ chữ "v2/" ở Base URL
        private readonly string _baseUrl = "https://dev-online-gateway.ghn.vn/shiip/public-api/";

        private async Task<JObject> SendRequestAsync(string endpoint, HttpMethod method, object body = null)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(_baseUrl);

                client.DefaultRequestHeaders.Add("Token", _apiToken);

                // Các API liên quan đến đơn hàng cần có ShopId
                if (endpoint.Contains("shipping-order"))
                {
                    client.DefaultRequestHeaders.Add("ShopId", _shopId);
                }

                HttpRequestMessage request = new HttpRequestMessage(method, endpoint);

                if (body != null)
                {
                    var json = JsonConvert.SerializeObject(body);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                HttpResponseMessage response = await client.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JObject.Parse(responseContent);
                }
                else
                {
                    throw new Exception($"Lỗi GHN API ({response.StatusCode}): {responseContent}");
                }
            }
        }

        // ==========================================================
        // 1. MASTER DATA: TỈNH - HUYỆN - XÃ
        // ==========================================================

        public async Task<JArray> GetProvincesAsync()
        {
            var response = await SendRequestAsync("master-data/province", HttpMethod.Get);
            return (JArray)response["data"];
        }

        public async Task<JArray> GetDistrictsAsync(int provinceId)
        {
            // Trả lại nguyên bản: Gửi GET và đính kèm province_id lên thanh URL
            var response = await SendRequestAsync($"master-data/district?province_id={provinceId}", HttpMethod.Get);
            return (JArray)response["data"];
        }

        public async Task<JArray> GetWardsAsync(int districtId)
        {
            // Trả lại nguyên bản: Gửi GET và đính kèm district_id lên thanh URL
            var response = await SendRequestAsync($"master-data/ward?district_id={districtId}", HttpMethod.Get);
            return (JArray)response["data"];
        }

        // ==========================================================
        // 2. TÍNH PHÍ VẬN CHUYỂN (ĐÃ SỬA: BỔ SUNG v2/ VÀO TRƯỚC)
        // ==========================================================

        public async Task<decimal> CalculateFeeAsync(int toDistrictId, string toWardCode, int weightInGrams)
        {
            var body = new
            {
                service_type_id = 2,
                //from_district_id = 1442, // THÊM DÒNG NÀY: Mã Quận 1, TP.HCM (Kho gửi hàng)
                to_district_id = toDistrictId,
                to_ward_code = toWardCode,
                weight = weightInGrams,
                length = 20,
                width = 20,
                height = 10
            };

            // Gọi API tính phí
            var response = await SendRequestAsync("v2/shipping-order/fee", HttpMethod.Post, body);
            return response["data"]["total"].Value<decimal>();
        }

        // ==========================================================
        // 3. TẠO ĐƠN HÀNG LÊN HỆ THỐNG GHN (ĐÃ SỬA: BỔ SUNG v2/ VÀO TRƯỚC)
        // ==========================================================

        public async Task<string> CreateOrderAsync(string toName, string toPhone, string toAddress, string toWardCode, int toDistrictId, int weight, decimal codAmount, List<object> items)
        {
            var body = new
            {
                payment_type_id = 2,
                note = "Đơn hàng từ Web bán linh kiện điện tử TNC",
                required_note = "CHOXEMHANGKHONGTHU",
                to_name = toName,
                to_phone = toPhone,
                to_address = toAddress,
                to_ward_code = toWardCode,
                to_district_id = toDistrictId,
                cod_amount = codAmount,
                weight = weight,
                length = 20,
                width = 20,
                height = 10,
                service_type_id = 2,
                items = items
            };

            // Gọi API tạo đơn hàng
            var response = await SendRequestAsync("v2/shipping-order/create", HttpMethod.Post, body);
            return response["data"]["order_code"].ToString();
        }
    }
}