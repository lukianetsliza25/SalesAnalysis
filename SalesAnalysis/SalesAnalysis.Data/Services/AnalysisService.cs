using Microsoft.EntityFrameworkCore;
using SalesAnalysis.Data;
using SalesAnalysis.Core.Models;
using SalesAnalysis.Core.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Text.Json;

namespace SalesAnalysis.Data.Services
{
    public class AnalysisService
    {
        private readonly SalesDbContext _context;

        // Використовуємо пряму ін'єкцію контексту для стабільної роботи з БД
        public AnalysisService(SalesDbContext context)
        {
            _context = context;
        }

        // -----------------------------------------------------
        // 1. Метод обчислення загального доходу користувача
        public async Task<decimal> GetTotalRevenueAsync(int userId)
        {
            var query = _context.Transactions.Where(t => t.UserId == userId);

            if (!await query.AnyAsync()) return 0m;

            // Використовуємо SumAsync для ефективного обчислення на стороні БД
            return await query.SumAsync(t => t.Revenue);
        }

        // -----------------------------------------------------
        // 2. Метод отримання загальної кількості транзакцій користувача
        public async Task<int> GetTotalTransactionsAsync(int userId)
        {
            return await _context.Transactions
                .Where(t => t.UserId == userId)
                .CountAsync();
        }

        // -----------------------------------------------------
        // 3. Метод формування RFM-даних для кластеризації клієнтів
        public async Task<List<CustomerData>> GetCustomerClusteringDataAsync(int userId)
        {
            var allTransactions = await _context.Transactions
                .Where(t => t.UserId == userId)
                .ToListAsync();

            if (!allTransactions.Any()) return new List<CustomerData>();

            // Визначаємо "сьогодні" як день після останньої транзакції для Recency
            var latestDate = allTransactions.Max(t => t.Date);
            var today = latestDate.AddDays(1);

            return allTransactions
                .GroupBy(t => t.CustomerId)
                .Select(g => new CustomerData
                {
                    CustomerId = g.Key,
                    // Monetary: сума витрат
                    TotalSpent = (float)g.Sum(t => t.Revenue),
                    // Frequency: кількість покупок
                    PurchaseFrequency = g.Count(),
                    // Recency: дні з останньої покупки
                    DaysSinceLastPurchase = (float)(today - g.Max(t => t.Date)).TotalDays
                })
                .ToList();
        }

        // -----------------------------------------------------
        // 4. Метод агрегації продажів за місяцями (для графіка та ML)
        public async Task<List<SalesDataPoint>> GetMonthlySalesDataAsync(int userId)
        {
            var allTransactions = await _context.Transactions
                .Where(t => t.UserId == userId)
                .ToListAsync();

            if (!allTransactions.Any()) return new List<SalesDataPoint>();

            return allTransactions
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select((g, index) => new SalesDataPoint
                {
                    // TimeIndex: порядковий номер для тренду (1, 2, 3...)
                    TimeIndex = index + 1,
                    // MonthOfYear: циклічний номер місяця для сезонності (1-12)
                    MonthOfYear = (float)g.Key.Month,
                    // SalesAmount: сума продажів
                    SalesAmount = (float)g.Sum(t => t.Revenue)
                })
                .ToList();
        }

        // -----------------------------------------------------
        // 5. Метод агрегації продажів для конкретного товару
        public async Task<List<SalesDataPoint>> GetMonthlySalesByProductAsync(string productId, int userId)
        {
            var productTransactions = await _context.Transactions
                .Where(t => t.ProductId == productId && t.UserId == userId)
                .ToListAsync();

            if (!productTransactions.Any()) return new List<SalesDataPoint>();

            return productTransactions
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select((g, index) => new SalesDataPoint
                {
                    TimeIndex = index + 1,
                    MonthOfYear = (float)g.Key.Month,
                    SalesAmount = (float)g.Sum(t => t.Revenue)
                })
                .ToList();
        }

        // -----------------------------------------------------
        // 6. Метод обчислення розширених місячних KPI
        public async Task<List<MonthlyKpiData>> GetMonthlyKpiDataAsync(int userId)
        {
            var all = await _context.Transactions
                .Where(t => t.UserId == userId)
                .ToListAsync();

            if (!all.Any()) return new List<MonthlyKpiData>();

            var grouped = all
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new MonthlyKpiData
                {
                    MonthIndex = $"{g.Key.Year}-{g.Key.Month:D2}",
                    TotalRevenue = (float)g.Sum(x => x.Revenue),
                    TotalTransactions = g.Count(),
                    UniqueCustomers = g.Select(x => x.CustomerId).Distinct().Count()
                })
                .ToList();

            // Розрахунок похідних метрик
            foreach (var m in grouped)
            {
                m.AverageOrderValue = m.TotalTransactions > 0 ? m.TotalRevenue / m.TotalTransactions : 0;
                m.CustomerSpend = m.UniqueCustomers > 0 ? m.TotalRevenue / m.UniqueCustomers : 0;
                m.Frequency = m.UniqueCustomers > 0 ? (float)m.TotalTransactions / m.UniqueCustomers : 0;
            }

            return grouped;
        }

        // -----------------------------------------------------
        // 7. Метод збереження результатів аналізу (Прогнозів/Кластерів)
        public async Task SaveAnalysisResultAsync(int userId, string productId, string type, object result)
        {
            // Видаляємо попередній аналіз такого ж типу перед збереженням нового
            var existing = _context.SavedAnalyses
                .Where(a => a.UserId == userId && a.AnalysisType == type && a.ProductId == productId);

            _context.SavedAnalyses.RemoveRange(existing);

            var saved = new SavedAnalysis
            {
                UserId = userId,
                ProductId = productId,
                AnalysisType = type,
                ResultJson = JsonSerializer.Serialize(result),
                CreatedAt = DateTime.UtcNow
            };

            await _context.SavedAnalyses.AddAsync(saved);
            await _context.SaveChangesAsync(); // Гарантоване збереження в БД
        }

        // -----------------------------------------------------
        // 8. Метод отримання останнього збереженого результату
        public async Task<string> GetLastAnalysisResultAsync(int userId, string type)
        {
            var analysis = await _context.SavedAnalyses
                .Where(a => a.UserId == userId && a.AnalysisType == type)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            return analysis?.ResultJson;
        }
    }
}