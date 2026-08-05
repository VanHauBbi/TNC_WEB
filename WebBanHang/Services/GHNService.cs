using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Configuration;
using System.Net.Http.Headers;

namespace WebBanHang.Services
{
    public class GHNService
    {
        private readonly string _apiToken = ConfigurationManager.AppSettings["GhnApiToken"];
        private readonly string _shopId = ConfigurationManager.AppSettings["GhnShopId"];
        private readonly string _baseUrl = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["GhnApiBaseUrl"])
            ? "https://online-gateway.ghn.vn/shiip/public-api/"
            : ConfigurationManager.AppSettings["GhnApiBaseUrl"].TrimEnd('/') + "/";

        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private async Task<JObject> SendRequestAsync(string endpoint, HttpMethod method, object body = null)
        {
            if (string.IsNullOrWhiteSpace(_apiToken))
                throw new ConfigurationErrorsException("GHN chưa được cấu hình. Hãy thêm GhnApiToken vào AppSettings.Local.config.");

            if (endpoint.Contains("shipping-order") && string.IsNullOrWhiteSpace(_shopId))
                throw new ConfigurationErrorsException("GHN chưa được cấu hình. Hãy thêm GhnShopId vào AppSettings.Local.config.");

            using (var request = new HttpRequestMessage(method, new Uri(new Uri(_baseUrl), endpoint)))
            {
                request.Headers.TryAddWithoutValidation("Token", _apiToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (endpoint.Contains("shipping-order"))
                    request.Headers.TryAddWithoutValidation("ShopId", _shopId);

                if (body != null)
                {
                    var json = JsonConvert.SerializeObject(body);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using (HttpResponseMessage response = await HttpClient.SendAsync(request))
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    JObject result;

                    try
                    {
                        result = JObject.Parse(responseContent);
                    }
                    catch (JsonException)
                    {
                        throw new Exception("GHN trả về dữ liệu không hợp lệ. Vui lòng thử lại sau.");
                    }

                    if (!response.IsSuccessStatusCode || result["code"]?.Value<int>() != 200)
                    {
                        string message = result["message"]?.ToString();
                        throw new Exception(string.IsNullOrWhiteSpace(message)
                            ? $"Không thể kết nối GHN ({(int)response.StatusCode})."
                            : "GHN: " + message);
                    }

                    return result;
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
            var response = await SendRequestAsync("master-data/district", HttpMethod.Post, new
            {
                province_id = provinceId
            });
            return (JArray)response["data"];
        }

        public async Task<JArray> GetWardsAsync(int districtId)
        {
            var response = await SendRequestAsync("master-data/ward", HttpMethod.Post, new
            {
                district_id = districtId
            });
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
