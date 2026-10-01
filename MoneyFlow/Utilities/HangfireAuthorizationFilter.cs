using Hangfire;
using Hangfire.AspNetCore;
using Hangfire.Dashboard;

namespace MoneyFlow.Utilities
{
    // Filtro de autorización para el dashboard de Hangfire.
    // Solo permite acceso a usuarios autenticados de la aplicación.
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();
            return httpContext?.User?.Identity?.IsAuthenticated == true;
        }
    }
}
