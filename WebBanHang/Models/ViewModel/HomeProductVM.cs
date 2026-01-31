using PagedList;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebBanHang.Models.ViewModel
{
    public class HomeProductVM
    {
        public string SearchTerm { get; set; }

        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;

        public List<Product> FeaturedProducts { get; set; }
        public List<Product> NewProducts { get; set; }


        // Danh sách sản phẩm có phân trang
        public IPagedList<Product> Products { get; set; }

        // Danh sách danh mục (nếu cần hiển thị)
        public IEnumerable<Category> Categories { get; set; }
    }
}