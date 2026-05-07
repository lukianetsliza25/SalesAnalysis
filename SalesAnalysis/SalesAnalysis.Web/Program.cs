// SalesAnalysis.Web/Program.cs (Фрагмент конфігурації)
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SalesAnalysis.Data;
using SalesAnalysis.Data.Services;
using SalesAnalysis.ML.Services;

var builder = WebApplication.CreateBuilder(args);

// --- 1. Налаштування підключення до SQLite ---
builder.Services.AddDbContext<SalesDbContext>(options =>
{
    // Фінальний рядок підключення для SQLite (можна залишити Data Source=SalesData.db)
    options.UseNpgsql(
    builder.Configuration.GetConnectionString("DefaultConnection"));
});
builder.Services.AddIdentity<IdentityUser<int>, IdentityRole<int>>(options => {
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
})
.AddEntityFrameworkStores<SalesDbContext>();

builder.Services.ConfigureApplicationCookie(options => {
    options.LoginPath = "/Account/Auth"; // Змінено з /Login на /Auth
    options.LogoutPath = "/Account/Auth";
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
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100_000_000;
});

var app = builder.Build();

CreateDbIfNotExists(app);

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Auth}/{id?}");

app.Run();
// --- 3. ГАРАНТОВАНЕ СТВОРЕННЯ БАЗИ ДАНИХ ---


// Встановлюємо стартовий маршрут на Dashboard


// --- ДОПОМІЖНИЙ МЕТОД ---
void CreateDbIfNotExists(IHost host)
{
    using (var scope = host.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<SalesDbContext>();
            context.Database.Migrate();
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred creating the DB.");
        }
    }
}