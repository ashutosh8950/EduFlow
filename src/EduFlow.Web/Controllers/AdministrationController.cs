using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Web.Controllers;

[Authorize(Policy = "AdministratorOnly")]
public sealed class AdministrationController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}