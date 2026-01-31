using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using WebBanHang.Models; // Cần thiết cho Product Entity
using PagedList; // Cần thiết cho IPagedList

namespace WebBanHang.Models.ViewModel
{
    public class ProductSearchVM
    {
        // 1. THUỘC TÍNH TÌM KIẾM & LỌC
        public string searchTerm { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }

        // 2. THUỘC TÍNH SẮP XẾP (Đã giữ lại tên sortOrder chuẩn)
        // Đây là thuộc tính Razor View và Controller dùng để lưu trạng thái sắp xếp.
        public string sortOrder { get; set; }

        // 3. THUỘC TÍNH PHÂN TRANG

        // Thuộc tính lưu trữ trang hiện tại (nullable int là đúng)
        // Dùng tên 'page' vì nó khớp với tham số truyền từ PagedList.Mvc
        public int? page { get; set; }

        // Thuộc tính lưu trữ kích thước trang (pageSize)
        // Đây là thuộc tính bạn đặt tên là PagedList, giá trị mặc định là 10.
        public int pageSize { get; set; } = 10;

        // 4. DANH SÁCH SẢN PHẨM (Kết quả)
        // Đây là thuộc tính chứa dữ liệu hiển thị trên View.
        public IPagedList<Product> products { get; set; }
    }
}