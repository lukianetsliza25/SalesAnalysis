using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SalesAnalysis.Core.Entities;
using SalesAnalysis.Data;
using SalesAnalysis.Data.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.EntityFrameworkCore;

// Позначення класу як набору модульних тестів NUnit
[TestFixture]
public class AnalysisServiceTests
{
    // Контейнер залежностей для тестового середовища
    private ServiceProvider _serviceProvider;

    // Корінь InMemory-бази для спільного використання в межах тесту
    private InMemoryDatabaseRoot _dbRoot;

    // Метод ініціалізації, виконується перед кожним тестом
    [SetUp]
    public void SetUp()
    {
        // Створення кореня InMemory-бази даних
        _dbRoot = new InMemoryDatabaseRoot();

        // Налаштування контейнера залежностей
        var services = new ServiceCollection();

        // Реєстрація DbContext з InMemory-провайдером
        services.AddDbContext<SalesDbContext>(o =>
            o.UseInMemoryDatabase("AnalysisDb", _dbRoot));

        // Побудова ServiceProvider
        _serviceProvider = services.BuildServiceProvider();

        // Заповнення тестової бази початковими даними
        Seed();
    }

    // Очищення ресурсів після виконання кожного тесту
    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
    }

    // Метод підготовки тестових транзакційних даних
    private void Seed()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        ctx.Transactions.AddRange(new[]
        {
        new Transaction { UserId = 1, CustomerId = "C1", Revenue = 100, Date = new DateTime(2024,1,10) },
        new Transaction { UserId = 1, CustomerId = "C1", Revenue = 50,  Date = new DateTime(2024,2,10) },
        new Transaction { UserId = 1, CustomerId = "C2", Revenue = 200, Date = new DateTime(2024,1,15) }
    });
        ctx.SaveChanges();
    }

    // -----------------------------------------------------
    // Тест перевірки обчислення загального доходу

    [Test]
    public async Task TotalRevenue_IsCalculatedCorrectly()
    {
        var service = new AnalysisService(_serviceProvider);
        // Передаємо UserId = 1, бо в Seed() ми тепер маємо додавати цей ID
        var result = await service.GetTotalRevenueAsync(1);
        Assert.AreEqual(350m, result);
    }

    [Test]
    public async Task MonthlyAggregation_ReturnsCorrectCount()
    {
        var service = new AnalysisService(_serviceProvider);
        var data = await service.GetMonthlySalesDataAsync(1); // Передаємо UserId = 1
        Assert.AreEqual(2, data.Count);
    }

    [Test]
    public async Task RfmData_ComputedCorrectly()
    {
        var service = new AnalysisService(_serviceProvider);
        var rfm = await service.GetCustomerClusteringDataAsync(1); // Передаємо UserId = 1
        Assert.AreEqual(2, rfm.Count);
    }
}
