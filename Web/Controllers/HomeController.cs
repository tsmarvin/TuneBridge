using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Web.Models;

namespace TuneBridge.Web.Controllers {
    /// <summary>
    /// Controller for the main web application pages.
    /// </summary>
    public class HomeController : Controller {
        private readonly ILogger<HomeController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="HomeController"/> class.
        /// </summary>
        /// <param name="logger">The logger for recording diagnostic information.</param>
        public HomeController( ILogger<HomeController> logger ) {
            _logger = logger;
        }

        /// <summary>
        /// Displays the home/index page.
        /// </summary>
        /// <returns>The index view.</returns>
        public IActionResult Index( ) => View( );

        /// <summary>
        /// Displays the privacy policy page.
        /// </summary>
        /// <returns>The privacy view.</returns>
        public IActionResult Privacy( ) => View( );

        /// <summary>
        /// Displays the error page.
        /// </summary>
        /// <returns>The error view with diagnostic information.</returns>
        [ResponseCache( Duration = 0, Location = ResponseCacheLocation.None, NoStore = true )]
        public IActionResult Error( ) {
            return View( new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier } );
        }
    }
}
