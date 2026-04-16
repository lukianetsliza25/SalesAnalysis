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
        // Створення області видимості для DbContext
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        // Додавання тестових транзакцій
        ctx.Transactions.AddRange(new[]
        {
            // Транзакції клієнта C1 (два різні місяці)
            new Transaction
            {
                CustomerId = "C1",
                ProductId = "P1",
                Date = new DateTime(2024,1,10),
                UnitPrice = 50,
                Quantity = 2,
                Revenue = 100
            },
            new Transaction
            {
                CustomerId = "C1",
                ProductId = "P2",
                Date = new DateTime(2024,2,10),
                UnitPrice = 25,
                Quantity = 2,
                Revenue = 50
            },

            // Транзакція клієнта C2
            new Transaction
            {
                CustomerId = "C2",
                ProductId = "P3",
                Date = new DateTime(2024,1,15),
                UnitPrice = 100,
                Quantity = 2,
                Revenue = 200
            }
        });

        // Збереження даних у InMemory-базі
        ctx.SaveChanges();
    }

    // -----------------------------------------------------
    // Тест перевірки обчислення загального доходу

    [Test]
    public async Task TotalRevenue_IsCalculatedCorrectly()
    {
        // Створення сервісу аналізу
        var service = new AnalysisService(_serviceProvider);

        // Виклик методу обчислення загального доходу
        var result = await service.GetTotalRevenueAsync();

        // Перевірка очікуваного значення
        Assert.AreEqual(350m, result);
    }

    // -----------------------------------------------------
    // Тест перевірки місячної агрегації продажів

    [Test]
    public async Task MonthlyAggregation_ReturnsCorrectCount()
    {
        var service = new AnalysisService(_serviceProvider);

        // Отримання агрегованих місячних даних
        var data = await service.GetMonthlySalesDataAsync();

        // Очікується два місяці з даними
        Assert.AreEqual(2, data.Count);
    }

    // -----------------------------------------------------
    // Тест перевірки коректності RFM-метрик

    [Test]
    public async Task RfmData_ComputedCorrectly()
    {
        var service = new AnalysisService(_serviceProvider);

        // Отримання RFM-даних клієнтів
        var rfm = await service.GetCustomerClusteringDataAsync();

        // Перевірка кількості унікальних клієнтів
        Assert.AreEqual(2, rfm.Count);

        // Отримання даних клієнта C1
        var c1 = rfm.Single(x => x.CustomerId == "C1");

        // Перевірка сумарних витрат
        Assert.AreEqual(150f, c1.TotalSpent);

        // Перевірка кількості покупок
        Assert.AreEqual(2, c1.PurchaseFrequency);

        // Перевірка коректності розрахунку давності останньої покупки
        Assert.GreaterOrEqual(c1.DaysSinceLastPurchase, 0);
    }
}
