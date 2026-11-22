using System.Text.RegularExpressions;
using CoCoReTrade.Web.Demo;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Controllers;

/// <summary>Reference photography is available only in the local coursework demo.</summary>
public sealed partial class EditorialImagesController(DemoCatalog demo, IWebHostEnvironment environment) : Controller
{
    [HttpGet("/images/editorial/demo/{name}")]
    [ResponseCache(NoStore = true)]
    public IActionResult Get(string name)
    {
        if (!demo.Enabled || !DemoCatalog.IsLocalHost(Request.Host.Host) || !SafeName().IsMatch(name))
            return NotFound();
        var path = Path.Combine(environment.ContentRootPath, "PrivateUploads", "editorial", "demo", name);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "image/webp");
    }

    [GeneratedRegex("^[a-z0-9-]+\\.webp$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
