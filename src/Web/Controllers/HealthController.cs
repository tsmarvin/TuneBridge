using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Web.Controllers {
    /// <summary>
    /// Controller for exposing detailed server health metrics.
    /// Access is restricted to internal network by HealthEndpointAuthorizationMiddleware.
    /// </summary>
    public class HealthController : Controller {
        private readonly IServerHealthMonitor? _healthMonitor;

        /// <summary>
        /// Initializes a new instance of the <see cref="HealthController"/> class.
        /// </summary>
        /// <param name="healthMonitor">Optional server health monitor service.</param>
        public HealthController( IServerHealthMonitor? healthMonitor = null ) {
            _healthMonitor = healthMonitor;
        }

        /// <summary>
        /// Returns detailed health metrics including error rates and request counts.
        /// This endpoint is only accessible from internal networks (Docker, localhost).
        /// </summary>
        /// <returns>JSON object with health metrics.</returns>
        [HttpGet( "/health/detailed" )]
        [ResponseCache( Duration = 0, Location = ResponseCacheLocation.None, NoStore = true )]
        public IActionResult Detailed( ) {
            if (_healthMonitor == null) {
                return Ok( new {
                    status = "healthy",
                    timestamp = DateTime.UtcNow,
                    message = "Health monitoring not configured"
                } );
            }

            bool isHealthy = _healthMonitor.IsHealthy( );
            return Ok( new {
                status = isHealthy ? "healthy" : "unhealthy",
                timestamp = DateTime.UtcNow,
                errorRate = _healthMonitor.CurrentErrorRate,
                totalRequests = _healthMonitor.TotalRequests,
                totalErrors = _healthMonitor.TotalErrors,
                isHealthy
            } );
        }
    }
}
