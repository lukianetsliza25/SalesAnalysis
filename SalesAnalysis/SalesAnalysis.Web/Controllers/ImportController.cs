// SalesAnalysis.Web/Controllers/ImportController.cs
using Microsoft.AspNetCore.Mvc;
using SalesAnalysis.Data.Services;
using System.IO;
using System.Threading.Tasks;
using System;
using SalesAnalysis.Core.Entities;
using System.Linq;

public class ImportController : Controller
{
    private readonly ImportService _importService;

    public ImportController(ImportService importService)
    {
        _importService = importService;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            ViewBag.Message = "Помилка: Файл не обрано або він порожній.";
            return View("Index");
        }

        try
        {
            int importedCount;
            using (var stream = file.OpenReadStream())
            {
                importedCount = await _importService.ImportTransactionsFromCsvAsync(stream);
            }

            if (importedCount > 0)
            {
                await Task.Delay(1000);

                return RedirectToAction("Index", "Dashboard");
            }
            else
            {
                ViewBag.Message = "Попередження: Імпортовано 0 транзакцій.";
            }
        }
        catch (Exception ex)
        {
            ViewBag.Message = $"Помилка імпорту: {ex.Message}";
        }

        return View("Index");
    }
}