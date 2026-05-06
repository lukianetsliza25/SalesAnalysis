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
            ViewBag.Message = "Помилка: Файл не обрано.";
            return View("Index");
        }

        try
        {
            // 1. Отримуємо ID користувача спочатку
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier).Value);

            int importedCount;
            using (var stream = file.OpenReadStream())
            {
                // 2. Викликаємо імпорт ТІЛЬКИ ОДИН РАЗ і передаємо userId
                importedCount = await _importService.ImportTransactionsFromCsvAsync(stream, userId);
            }

            if (importedCount > 0)
            {
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