using System.Web;
using System.Web.Mvc;
using WebBanHang.Security;

namespace WebBanHang
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
            filters.Add(new AdminAreaAuthorizationFilter());
        }
    }
}
