using NUnit.Framework;
using SalesAnalysis.ML.Services;
using SalesAnalysis.Core.Models;
using System.Collections.Generic;
using System;
using System.Linq;

// Позначення класу як набору модульних тестів сервісу прогнозування
[TestFixture]
public class PredictionServiceTests
{
    // Екземпляр сервісу прогнозування продажів
    private PredictionService _service;

    // Ініціалізація сервісу перед кожним тестом
    [SetUp]
    public void SetUp()
    {
        _service = new PredictionService();
    }

    // -----------------------------------------------------
    // Тест перевірки валідації мінімальної кількості даних

    [Test]
    public void Train_Throws_WhenLessThan4Points()
    {
        // Формування недостатнього набору даних (менше 4 точок)
        var data = new List<SalesDataPoint>
        {
            new SalesDataPoint { TimeIndex = 1, SalesAmount = 100 },
            new SalesDataPoint { TimeIndex = 2, SalesAmount = 200 },
            new SalesDataPoint { TimeIndex = 3, SalesAmount = 300 }
        };

        // Перетворення даних у формат IDataView для ML.NET
        var dv = _service.MLContext.Data.LoadFromEnumerable(data);

        // Перевірка, що навчання моделі викликає виняток
        Assert.Throws<InvalidOperationException>(() =>
            _service.TrainAndSaveModel(dv));
    }

    // -----------------------------------------------------
    // Тест перевірки коректності прогнозу на декілька періодів

    [Test]
    public void Forecast_ReturnsCorrectCount_AndNonNegative()
    {
        // Формування коректного набору даних для навчання моделі
        var data = new List<SalesDataPoint>
        {
            new SalesDataPoint { TimeIndex = 1, SalesAmount = 100 },
            new SalesDataPoint { TimeIndex = 2, SalesAmount = 150 },
            new SalesDataPoint { TimeIndex = 3, SalesAmount = 200 },
            new SalesDataPoint { TimeIndex = 4, SalesAmount = 250 }
        };

        // Навчання моделі прогнозування
        var model = _service.TrainAndSaveModel(
            _service.MLContext.Data.LoadFromEnumerable(data));

        // Генерація прогнозу на 12 майбутніх періодів
        var forecast = _service.PredictNPeriods(model, 5, 12);

        // Перевірка кількості прогнозних значень
        Assert.AreEqual(12, forecast.Count);

        // Перевірка, що всі прогнозні значення є невід’ємними
        Assert.IsTrue(forecast.All(x => x >= 0));
    }
}
