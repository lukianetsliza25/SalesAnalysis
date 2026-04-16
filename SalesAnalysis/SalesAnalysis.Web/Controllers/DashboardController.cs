// SalesAnalysis.Web/Controllers/DashboardController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.ML;
using SalesAnalysis.Core.Models;
using SalesAnalysis.Data.Services;
using SalesAnalysis.ML.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class DashboardController : Controller
{
    private readonly AnalysisService _analysisService;
    private readonly ClusteringService _clusteringService;
    private readonly PredictionService _predictionService;

    private const int PAGE_SIZE = 30;
    private const int PREDICTION_PERIODS = 12;

    public DashboardController(
        AnalysisService analysisService,
        ClusteringService clusteringService,
        PredictionService predictionService)
    {
        _analysisService = analysisService;
        _clusteringService = clusteringService;
        _predictionService = predictionService; 
    }

    public async Task<IActionResult> Index()
    {

        // KPI
        ViewBag.TotalRevenue = await _analysisService.GetTotalRevenueAsync();
        ViewBag.TotalTransactions = await _analysisService.GetTotalTransactionsAsync();

        var allData = await _analysisService.GetCustomerClusteringDataAsync();

        // Кількість унікальних клієнтів
        int uniqueCustomers = allData.Count;

        // Середній чек
        decimal averageOrderValue = 0;
        if (ViewBag.TotalTransactions > 0)
        {
            averageOrderValue = ViewBag.TotalRevenue / ViewBag.TotalTransactions;
        }

        // Середні витрати на клієнта
        decimal avgCustomerSpend = 0;
        if (uniqueCustomers > 0)
        {
            avgCustomerSpend = ViewBag.TotalRevenue / uniqueCustomers;
        }

        // Середня частота покупок
        float avgFrequency = 0;
        if (uniqueCustomers > 0)
        {
            avgFrequency = (float)ViewBag.TotalTransactions / uniqueCustomers;
        }

        // 5. Найкращий і найгірший місяць продажів
        var monthlyData = await _analysisService.GetMonthlySalesDataAsync();
        var monthlyKpi = await _analysisService.GetMonthlyKpiDataAsync();
        ViewBag.KpiHistory = monthlyKpi;

        float bestMonth = 0, worstMonth = 0;
        string bestMonthName = "-", worstMonthName = "-";

        if (monthlyData.Any())
        {
            var best = monthlyData.OrderByDescending(m => m.SalesAmount).First();
            var worst = monthlyData.OrderBy(m => m.SalesAmount).First();

            bestMonth = best.SalesAmount;
            worstMonth = worst.SalesAmount;

            bestMonthName = $"Місяць #{best.Year}";
            worstMonthName = $"Місяць #{worst.Year}";
        }

        // --- Передаємо у View --- //
        ViewBag.UniqueCustomers = uniqueCustomers;
        ViewBag.AverageOrderValue = Math.Round(averageOrderValue, 2);
        ViewBag.AvgCustomerSpend = Math.Round(avgCustomerSpend, 2);
        ViewBag.AvgFrequency = Math.Round(avgFrequency, 2);

        ViewBag.BestMonth = bestMonth;
        ViewBag.BestMonthName = bestMonthName;

        ViewBag.WorstMonth = worstMonth;
        ViewBag.WorstMonthName = worstMonthName;


        // ---------------- 1. Отримуємо RFM-дані ----------------
        var data = await _analysisService.GetCustomerClusteringDataAsync();
        var result = new List<ClusteredCustomer>();

        if (data.Any())
        {
            // ---------------- 2. Пошук аномалій ----------------

            var anomalies = new List<CustomerData>();
            var normal = new List<CustomerData>();

            var spent = data.Select(x => x.TotalSpent)
                .Where(x => x > 0)
                .OrderBy(x => x)
                .ToList();
            var freq = data.Select(x => x.PurchaseFrequency)
                .OrderBy(x => x).ToList();

            float Percentile(List<float> list, double p)
            {
                if (list.Count == 0) return 0;
                double idx = (list.Count - 1) * p;
                int i = (int)idx;
                double frac = idx - i;
                return (float)(list[i] + (list[Math.Min(i + 1, list.Count - 1)] -
                    list[i]) * frac);
            }

            float p99Spent = Percentile(spent, 0.99);
            float p99Freq = Percentile(freq, 0.99);

            foreach (var c in data)
            {
                bool isAnomaly =
                    c.TotalSpent <= 0 ||
                    c.TotalSpent > p99Spent ||
                    c.PurchaseFrequency > p99Freq;

                if (isAnomaly) anomalies.Add(c);
                else normal.Add(c);
            }

            // ---------------- 3. Тренуємо модель на неаномальних ----------------

            var model = _clusteringService.TrainAndSaveModel(
                _clusteringService.MLContext.Data.LoadFromEnumerable(normal)
            );

            var predictions = normal
                .Select(c => (c, _clusteringService.Predict(model, c)))
                .ToList();

            // Групування результатів кластеризації за ідентифікатором кластера
            // та обчислення середніх значень RFM-метрик для кожного кластера
            var stats = predictions
                .GroupBy(p => (int)p.Item2.PredictedClusterId)
                .Select(g => new
                {
                    Id = g.Key, // Ідентифікатор кластера
                    // Середні витрати клієнтів
                    AvgSpent = g.Average(x => x.Item1.TotalSpent),
                    // Середня частота покупок
                    AvgFreq = g.Average(x => x.Item1.PurchaseFrequency),
                    // Середня давність останньої покупки
                    AvgRec = g.Average(x => x.Item1.DaysSinceLastPurchase)
                })
                .ToList();

            // Визначення логічних типів кластерів на основі RFM-характеристик

            // VIP-кластер — клієнти з найбільшими витратами та високою частотою покупок
            var vip = stats
                .OrderByDescending(x => x.AvgSpent + x.AvgFreq)
                .First().Id;

            // Рідкісний (або новий) кластер — клієнти з найбільшою давністю останньої покупки
            var rare = stats
                .OrderByDescending(x => x.AvgRec)
                .First().Id;

            // Середній кластер — решта клієнтів, що не належать до VIP або рідкісних
            var mid = stats
                .Select(x => x.Id)
                .Except(new[] { vip, rare })
                .FirstOrDefault();

            // Мапінг технічних ідентифікаторів кластерів у логічні категорії
            int MapCluster(int id)
            {
                if (id == vip) return 3;   // VIP-клієнти
                if (id == rare) return 1;  // Нові або рідкісні клієнти
                return 2;                  // Клієнти середнього сегмента
            }


            // ---------------- 5. Додаємо нормальні клієнти ----------------

            foreach (var (c, p) in predictions)
            {
                int cluster = (int)p.PredictedClusterId;
                int logicalId = MapCluster(cluster);

                string desc = logicalId switch
                {
                    3 => "Високоцінний (VIP)",
                    1 => "Новий/Рідкісний",
                    _ => "Середній"
                };

                result.Add(new ClusteredCustomer
                {
                    CustomerId = c.CustomerId,
                    TotalSpent = c.TotalSpent,
                    PurchaseFrequency = (int)c.PurchaseFrequency,
                    ClusterId = logicalId,
                    ClusterDescription = desc
                });
            }

            // ---------------- 6. Додаємо аномалії ----------------

            foreach (var a in anomalies)
            {
                result.Add(new ClusteredCustomer
                {
                    CustomerId = a.CustomerId,
                    TotalSpent = a.TotalSpent,
                    PurchaseFrequency = (int)a.PurchaseFrequency,
                    ClusterId = 0,
                    ClusterDescription = "Аномалія / Некоректні дані"
                });
            }


        }


        // ---------------- ПАГІНАЦІЯ ----------------

        int total = result.Count;
        int totalPages = (int)Math.Ceiling(total / (double)PAGE_SIZE);

        // Поточна сторінка
        int currentPage = 1;
        if (Request.Query.ContainsKey("page"))
        {
            int.TryParse(Request.Query["page"], out currentPage);
            if (currentPage < 1) currentPage = 1;
            if (currentPage > totalPages) currentPage = totalPages;
        }

        ViewBag.ClusteredCustomers = result
            .OrderBy(c => c.CustomerId)        // можна замінити на OrderByDescending(...)
            .Skip((currentPage - 1) * PAGE_SIZE)
            .Take(PAGE_SIZE)
            .ToList();

        ViewBag.TotalCustomers = total;
        ViewBag.CurrentPage = currentPage;
        ViewBag.TotalPages = totalPages;


        // 6. Прогнозування та Тональність
        var monthlyData1 = await _analysisService.GetMonthlySalesDataAsync();
        ViewBag.TotalMonths = monthlyData.Count;


        // Збір даних для графіку
        List<float> historyData = monthlyData.Select(d => d.SalesAmount).ToList();
        List<float> predictionData = new List<float>();

        if (monthlyData.Count >= 4)
        {
            var nextTimeIndex = monthlyData.Max(d => d.Year) + 1;

            try
            {
                var predictionModel = _predictionService.TrainAndSaveModel(
                    _predictionService.MLContext.Data.LoadFromEnumerable(monthlyData));

                // 1. Прогноз на 12 місяців
                predictionData = _predictionService.PredictNPeriods(
                    predictionModel, nextTimeIndex, PREDICTION_PERIODS);

                // 2. Перший прогноз (для картки KPI)
                ViewBag.NextMonthPrediction = predictionData.FirstOrDefault();
            }
            catch (Exception)
            {
                ViewBag.NextMonthPrediction = 450.00f;
            }
        }
        else
        {
            ViewBag.NextMonthPrediction = 0.0f;
        }

        // Передача даних у View у форматі JSON
        ViewBag.HistoryDataJson = JsonSerializer.Serialize(historyData);
        ViewBag.PredictionDataJson = JsonSerializer.Serialize(predictionData);


        return View();
    }


}
