// SalesAnalysis.Web/Program.cs (Фрагмент конфігурації)
using Microsoft.EntityFrameworkCore;
using SalesAnalysis.Data;
using SalesAnalysis.Data.Services;
using SalesAnalysis.ML.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// --- 1. Налаштування підключення до SQLite ---
builder.Services.AddDbContext<SalesDbContext>(options =>
{
    // Фінальний рядок підключення для SQLite (можна залишити Data Source=SalesData.db)
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// --- 2. Реєстрація Сервісів (Dependency Injection) ---
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<AnalysisService>();
builder.Services.AddSingleton<ClusteringService>();
builder.Services.AddSingleton<PredictionService>();

builder.Services.AddControllersWithViews();

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 104857600; // 100 MB
});

var app = builder.Build();

// --- 3. ГАРАНТОВАНЕ СТВОРЕННЯ БАЗИ ДАНИХ ---
CreateDbIfNotExists(app);

app.UseAuthorization();

// Встановлюємо стартовий маршрут на Dashboard
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();

// --- ДОПОМІЖНИЙ МЕТОД ---
void CreateDbIfNotExists(IHost host)
{
    using (var scope = host.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<SalesDbContext>();
            context.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred creating the DB.");
        }
    }
}