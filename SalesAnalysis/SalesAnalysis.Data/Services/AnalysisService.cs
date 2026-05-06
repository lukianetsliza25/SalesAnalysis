// SalesAnalysis.Data/Services/AnalysisService.cs
using Microsoft.EntityFrameworkCore;
using SalesAnalysis.Data;
using SalesAnalysis.Core.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.DependencyInjection;
using SalesAnalysis.Core.Entities;
using System.Text.Json;

namespace SalesAnalysis.Data.Services
{
    public class AnalysisService
    {
        // Провайдер сервісів для створення області видимості DbContext
        private readonly IServiceProvider _serviceProvider;

        // Конструктор з передаванням контейнера залежностей
        public AnalysisService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        // Допоміжний метод створення екземпляра SalesDbContext
        // Використовується для коректного керування життєвим циклом контексту
        private SalesDbContext GetContext()
        {
            return _serviceProvider
                .CreateScope()
                .ServiceProvider
                .GetRequiredService<SalesDbContext>();
        }

        // -----------------------------------------------------
        // Метод обчислення загального доходу
        public async Task<decimal> GetTotalRevenueAsync(int userId)
        {
            using (var context = GetContext())
            {
                // Якщо транзакції відсутні — повертаємо 0
                if (!await context.Transactions.AnyAsync())
                {
                    return 0m;
                }

                // Обчислення сумарного доходу на основі поля Revenue
                return await Task.Run(() =>
                    context.Transactions
                            .Where(t => t.UserId == userId) // ФІЛЬТР
                           .AsEnumerable()
                           .Sum(t => t.Revenue)
                );
            }
        }

        // -----------------------------------------------------
        // Метод отримання загальної кількості транзакцій
        public async Task<int> GetTotalTransactionsAsync(int userId)
        {
            using (var context = GetContext())
            {
                // Підрахунок кількості записів у таблиці транзакцій
                return await context.Transactions
                    .Where(t => t.UserId == userId) // ДОДАЙТЕ ЦЕЙ РЯДОК
                    .CountAsync();
            }
        }

        // -----------------------------------------------------
        // Метод формування RFM-даних для кластеризації клієнтів

        public async Task<List<CustomerData>> GetCustomerClusteringDataAsync(int userId)
        {
            using (var context = GetContext())
            {
                // Отримання всіх транзакцій з бази даних
                var allTransactions = await context.Transactions
                    .Where(t => t.UserId == userId) // ФІЛЬТР
                    .ToListAsync();

                // Якщо дані відсутні — повертаємо порожній список
                if (!allTransactions.Any())
                {
                    return new List<CustomerData>();
                }

                // Підготовка даних з коректним форматом дати
                var transactionsWithParsedDates = allTransactions
                    .Select(t => new
                    {
                        t.CustomerId,
                        t.Revenue,
                        Date = DateTime.Parse(t.Date.ToString())
                    })
                    .ToList();

                // Визначення останньої дати в наборі даних
                var latestDate = transactionsWithParsedDates.Max(t => t.Date);
                var today = latestDate.AddDays(1);

                // Групування транзакцій за клієнтами та обчислення RFM-метрик
                var rfmData = transactionsWithParsedDates
                    .GroupBy(t => t.CustomerId)
                    .Select(g => new CustomerData
                    {
                        // Ідентифікатор клієнта
                        CustomerId = g.Key,

                        // Monetary: сумарні витрати клієнта
                        TotalSpent = (float)g.Sum(t => t.Revenue),

                        // Frequency: кількість транзакцій
                        PurchaseFrequency = g.Count(),

                        // Recency: кількість днів з моменту останньої покупки
                        DaysSinceLastPurchase =
                            (float)(today - g.Max(t => t.Date)).TotalDays
                    })
                    .ToList();

                return rfmData;
            }
        }
        // Метод агрегації продажів для КОНКРЕТНОГО товару
        public async Task<List<SalesDataPoint>> GetMonthlySalesByProductAsync(string productId, int userId)
        {
            using var context = GetContext();
            var all = await context.Transactions
                .Where(t => t.ProductId == productId && t.UserId == userId).ToListAsync();

            if (!all.Any()) return new List<SalesDataPoint>();

            return all
                .Select(t => new { t.Revenue, Date = t.Date })
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select((g, index) => new SalesDataPoint
                {
                    TimeIndex = index + 1,
                    SalesAmount = (float)g.Sum(t => t.Revenue)
                })
                .ToList();
        }


        public async Task SaveAnalysisResultAsync(int userId, string productId, string type, object result)
        {
            using var context = GetContext();
            var saved = new SavedAnalysis
            {
                UserId = userId,
                ProductId = productId, // null якщо загальний аналіз
                AnalysisType = type,   // наприклад "SalesForecast" або "Clustering"
                ResultJson = JsonSerializer.Serialize(result),
                CreatedAt = DateTime.UtcNow
            };

            context.SavedAnalyses.Add(saved);
            await context.SaveChangesAsync();
        }
        // -----------------------------------------------------
        // Метод агрегації продажів за місяцями

        public async Task<List<SalesDataPoint>> GetMonthlySalesDataAsync(int userId)
        {
            using (var context = GetContext())
            {
                // Отримання всіх транзакцій
                var allTransactions = await context.Transactions
                    .Where(t => t.UserId == userId)
                    .ToListAsync();

                // Якщо дані відсутні — повертаємо порожній список
                if (!allTransactions.Any())
                {
                    return new List<SalesDataPoint>();
                }

                // Підготовка дат для групування
                var transactionsWithParsedDates = allTransactions
                    .Select(t => new
                    {
                        t.Revenue,
                        Date = DateTime.Parse(t.Date.ToString())
                    })
                    .ToList();

                // Групування транзакцій за роком і місяцем
                // та формування часової осі у вигляді індексу
                return transactionsWithParsedDates
                    .GroupBy(t => new { t.Date.Year, t.Date.Month })
                    .OrderBy(g => g.Key.Year)
                    .ThenBy(g => g.Key.Month)
                    .Select((g, index) => new SalesDataPoint
                    {
                        // Індекс періоду (використовується для прогнозування)
                        TimeIndex = index + 1,

                        // Сумарний обсяг продажів за місяць
                        SalesAmount = (float)g.Sum(t => t.Revenue)
                    })
                    .ToList();
            }
        }

        // -----------------------------------------------------
        // Метод обчислення розширених місячних KPI
        public async Task<List<MonthlyKpiData>> GetMonthlyKpiDataAsync(int userId)
        {
            using var context = GetContext();

            // Отримання всіх транзакцій
            var all = await context.Transactions
                .Where(t => t.UserId == userId)
                .ToListAsync();
            if (!all.Any()) return new List<MonthlyKpiData>();

            // Групування даних за місяцями
            var grouped = all
                .Select(t => new
                {
                    t.Revenue,
                    t.CustomerId,
                    Date = DateTime.Parse(t.Date.ToString())
                })
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .OrderBy(g => g.Key.Year)
                .ThenBy(g => g.Key.Month)
                .Select(g => new MonthlyKpiData
                {
                    // Ідентифікатор місяця
                    MonthIndex = $"{g.Key.Year}-{g.Key.Month}",

                    // Загальний дохід
                    TotalRevenue = (float)g.Sum(x => x.Revenue),

                    // Кількість транзакцій
                    TotalTransactions = g.Count(),

                    // Кількість унікальних клієнтів
                    UniqueCustomers =
                        g.Select(x => x.CustomerId).Distinct().Count()
                })
                .ToList();

            // Обчислення похідних KPI
            foreach (var m in grouped)
            {
                // Середній чек
                m.AverageOrderValue =
                    m.TotalTransactions > 0
                        ? m.TotalRevenue / m.TotalTransactions
                        : 0;

                // Середні витрати на клієнта
                m.CustomerSpend =
                    m.UniqueCustomers > 0
                        ? m.TotalRevenue / m.UniqueCustomers
                        : 0;

                // Середня частота покупок
                m.Frequency =
                    m.UniqueCustomers > 0
                        ? (float)m.TotalTransactions / m.UniqueCustomers
                        : 0;
            }

            return grouped;
        }

        public async Task<string> GetLastAnalysisResultAsync(int userId, string type)
        {
            using var context = GetContext();
            var analysis = await context.SavedAnalyses
                .Where(a => a.UserId == userId && a.AnalysisType == type)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            return analysis?.ResultJson;
        }
    }
}
