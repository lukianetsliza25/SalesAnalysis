// Підключення необхідних просторів імен для тестування,
// роботи з ASP.NET MVC, Entity Framework Core та NUnit
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SalesAnalysis.Core.Entities;
using SalesAnalysis.Core.Models;
using SalesAnalysis.Data;
using SalesAnalysis.Data.Services;
using SalesAnalysis.ML.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SalesAnalysis.Tests
{
    // Позначає клас як набір тестів NUnit
    [TestFixture]
    public class DashboardControllerTests
    {
        // Контейнер залежностей для імітації DI у тестах
        private ServiceProvider _serviceProvider;

        // Спільний корінь InMemory-бази для одного тесту
        private InMemoryDatabaseRoot _dbRoot;

        // -----------------------------------------------------
        // Метод підготовки мінімального набору даних,
        // необхідного для коректної роботи кластеризації та KPI
        private void SeedMinimumClusterData(SalesDbContext ctx)
        {
            ctx.Transactions.AddRange(new[]
            {
                // Клієнт C1 (2 транзакції)
                new Transaction
                {
                    CustomerId = "C1",
                    ProductId = "P1",
                    Date = new DateTime(2024,1,1),
                    Revenue = 100,
                    Quantity = 1
                },
                new Transaction
                {
                    CustomerId = "C1",
                    ProductId = "P2",
                    Date = new DateTime(2024,1,5),
                    Revenue = 120,
                    Quantity = 1
                },

                // Клієнт C2 (2 транзакції)
                new Transaction
                {
                    CustomerId = "C2",
                    ProductId = "P3",
                    Date = new DateTime(2024,1,2),
                    Revenue = 90,
                    Quantity = 1
                },
                new Transaction
                {
                    CustomerId = "C2",
                    ProductId = "P4",
                    Date = new DateTime(2024,1,6),
                    Revenue = 110,
                    Quantity = 1
                },

                // Клієнт C3 (2 транзакції)
                new Transaction
                {
                    CustomerId = "C3",
                    ProductId = "P5",
                    Date = new DateTime(2024,1,3),
                    Revenue = 95,
                    Quantity = 1
                },
                new Transaction
                {
                    CustomerId = "C3",
                    ProductId = "P6",
                    Date = new DateTime(2024,1,7),
                    Revenue = 105,
                    Quantity = 1
                },

                // Додатковий клієнт C4 для гарантії достатньої кількості даних
                new Transaction
                {
                    CustomerId = "C4",
                    ProductId = "P7",
                    Date = new DateTime(2024,1,4),
                    Revenue = 100,
                    Quantity = 1
                },
                new Transaction
                {
                    CustomerId = "C4",
                    ProductId = "P8",
                    Date = new DateTime(2024,1,8),
                    Revenue = 115,
                    Quantity = 1
                }
            });
        }

        // -----------------------------------------------------
        // Метод ініціалізації тестового середовища,
        // виконується перед кожним тестом
        [SetUp]
        public void SetUp()
        {
            // Ініціалізація спільної InMemory-бази
            _dbRoot = new InMemoryDatabaseRoot();

            // Налаштування контейнера залежностей
            var services = new ServiceCollection();

            // Реєстрація DbContext з InMemory-провайдером
            services.AddDbContext<SalesDbContext>(o =>
                o.UseInMemoryDatabase("DashboardDb", _dbRoot));

            // Реєстрація сервісів, які використовує контролер
            services.AddScoped<AnalysisService>();
            services.AddSingleton<ClusteringService>();
            services.AddSingleton<PredictionService>();

            // Створення ServiceProvider
            _serviceProvider = services.BuildServiceProvider();
        }

        // -----------------------------------------------------
        // Очищення ресурсів після кожного тесту
        [TearDown]
        public void TearDown()
        {
            _serviceProvider.Dispose();
        }

        // -----------------------------------------------------
        // Допоміжний метод створення контролера з тестовими даними
        public DashboardController CreateController(Action<SalesDbContext> seed)
        {
            // Наповнення тестової бази даних
            using (var scope = _serviceProvider.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
                seed(ctx);
                ctx.SaveChanges();
            }

            // Створення контролера з необхідними залежностями
            var controller = new DashboardController(
                _serviceProvider.GetRequiredService<AnalysisService>(),
                _serviceProvider.GetRequiredService<ClusteringService>(),
                _serviceProvider.GetRequiredService<PredictionService>()
            );

            // Ініціалізація HTTP-контексту для контролера
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return controller;
        }

        // -----------------------------------------------------
        // Тест: метод Index не повинен викликати винятки за відсутності даних
        [Test]
        public async Task Index_NoData_DoesNotThrow()
        {
            var controller = CreateController(ctx => { });

            var result = await controller.Index();

            Assert.IsInstanceOf<ViewResult>(result);
        }

        // -----------------------------------------------------
        // Тест: перевірка коректного обчислення KPI
        [Test]
        public async Task Index_ComputesBasicKpi()
        {
            var controller = CreateController(ctx =>
            {
                SeedMinimumClusterData(ctx);
            });

            await controller.Index();

            // Перевірка основних KPI
            Assert.AreEqual(835m, controller.ViewBag.TotalRevenue);
            Assert.AreEqual(8, controller.ViewBag.TotalTransactions);
            Assert.AreEqual(4, controller.ViewBag.UniqueCustomers);
        }

        // -----------------------------------------------------
        // Тест: аномальні клієнти повинні потрапляти в кластер 0
        [Test]
        public async Task Index_Anomalies_AssignedToClusterZero()
        {
            var controller = CreateController(ctx =>
            {
                SeedMinimumClusterData(ctx);

                // Додавання аномальної транзакції
                ctx.Transactions.Add(new Transaction
                {
                    CustomerId = "AX",
                    ProductId = "PX",
                    Date = new DateTime(2024, 1, 5),
                    Revenue = 1_000_000,
                    Quantity = 1
                });
            });

            await controller.Index();

            var clustered =
                (List<ClusteredCustomer>)controller.ViewBag.ClusteredCustomers;

            Assert.IsTrue(clustered.Any(c => c.ClusterId == 0));
        }

        // -----------------------------------------------------
        // Тест: перевірка формування історії та прогнозу продажів
        [Test]
        public async Task Index_History_And_Prediction_AreGenerated()
        {
            var controller = CreateController(ctx =>
            {
                SeedMinimumClusterData(ctx);

                // Додавання додаткових місяців для прогнозування
                ctx.Transactions.AddRange(new[]
                {
                    new Transaction
                    {
                        CustomerId = "C1",
                        ProductId = "P1",
                        Date = new DateTime(2024,2,1),
                        Revenue = 110
                    },
                    new Transaction
                    {
                        CustomerId = "C1",
                        ProductId = "P1",
                        Date = new DateTime(2024,3,1),
                        Revenue = 130
                    },
                    new Transaction
                    {
                        CustomerId = "C1",
                        ProductId = "P1",
                        Date = new DateTime(2024,4,1),
                        Revenue = 140
                    }
                });
            });

            await controller.Index();

            // Перевірка формування даних для візуалізації
            Assert.IsNotNull(controller.ViewBag.HistoryDataJson);
            Assert.IsNotNull(controller.ViewBag.PredictionDataJson);
        }
    }
}
