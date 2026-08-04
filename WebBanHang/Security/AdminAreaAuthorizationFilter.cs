using System.Web.Mvc;

namespace WebBanHang.Security
{
    public class AdminAreaAuthorizationFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var area = filterContext.RouteData.DataTokens["area"] as string;
            if (!string.Equals(area, "Admin", System.StringComparison.OrdinalIgnoreCase)) return;

            var session = filterContext.HttpContext.Session;
            var isAdmin = session != null
                          && session["UserName"] != null
                          && string.Equals(session["UserRole"] as string, "A", System.StringComparison.OrdinalIgnoreCase);

            if (isAdmin) return;

            filterContext.Result = new RedirectToRouteResult(new System.Web.Routing.RouteValueDictionary(new
            {
                area = "",
                controller = "Account",
                action = "Login",
                returnUrl = filterContext.HttpContext.Request.RawUrl
            }));
        }
    }
}
