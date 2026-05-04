// SalesAnalysis.Web/Controllers/PredictionService.cs
using Microsoft.ML;
using SalesAnalysis.Core.Models;
using Microsoft.ML.Data;
using System.Linq;
using System.Collections.Generic;
using System;

namespace SalesAnalysis.ML.Services
{
    public class PredictionService
    {
        // Контекст ML.NET з фіксованим seed для відтворюваності результатів
        public MLContext MLContext { get; } = new MLContext(seed: 0);

        // Шлях до файлу збереженої моделі прогнозування
        private const string ModelPath = "sales_prediction_model.zip";

        // -----------------------------------------------------
        // Метод навчання регресійної моделі та збереження її на диск
        public ITransformer TrainAndSaveModel(IDataView trainingData)
        {
            // Перевірка мінімальної кількості даних для навчання моделі
            // Для коректного прогнозування необхідно щонайменше 4 точки
            if (trainingData.GetRowCount() < 4)
                throw new InvalidOperationException(
                    "Недостатньо точок даних для навчання. Потрібно мінімум 4.");

            // Формування конвеєра обробки даних і навчання моделі
            var pipeline =
                // Об’єднання індексу часу у вектор ознак
                MLContext.Transforms.Concatenate(
                    "Features", nameof(SalesDataPoint.TimeIndex))

                // Нормалізація ознак для стабільності навчання
                .Append(MLContext.Transforms.NormalizeMinMax("Features"))

                // Навчання регресійної моделі
                // Використовується Poisson-регресія для прогнозування кількісних значень
                .Append(MLContext.Regression.Trainers.LbfgsPoissonRegression(
                    labelColumnName: "Label",
                    featureColumnName: "Features"));

            // Навчання моделі на вхідних даних
            var model = pipeline.Fit(trainingData);

            // Збереження навченої моделі у файл
            MLContext.Model.Save(model, trainingData.Schema, ModelPath);

            return model;
        }

        // -----------------------------------------------------
        // Метод прогнозування одного наступного періоду

        public SalesPrediction Predict(
            ITransformer trainedModel,
            float nextTimeIndex)
        {
            // Створення PredictionEngine для виконання прогнозу
            var predictionEngine = MLContext.Model
                .CreatePredictionEngine<SalesDataPoint, SalesPrediction>(
                    trainedModel);

            // Формування вхідних даних для прогнозування
            var input = new SalesDataPoint
            {
                TimeIndex = nextTimeIndex
            };

            // Повернення прогнозного значення
            return predictionEngine.Predict(input);
        }

        // -----------------------------------------------------
        // Метод прогнозування продажів на N майбутніх періодів
        public List<float> PredictNPeriods(
            ITransformer trainedModel,
            float startNextIndex,
            int periods)
        {
            var results = new List<float>();
            // Створення PredictionEngine для багаторазового прогнозування
            var predictionEngine =
                MLContext.Model.CreatePredictionEngine<
                    SalesDataPoint, SalesPrediction>(trainedModel);

            // Генерація прогнозу для кожного наступного періоду
            for (int i = 0; i < periods; i++)
            {
                // Обчислення індексу наступного періоду
                var nextIndex = startNextIndex + i;

                var input = new SalesDataPoint
                {
                    TimeIndex = nextIndex
                };

                // Отримання прогнозного значення
                var prediction = predictionEngine.Predict(input);
                // Забезпечення невід’ємності прогнозу
                // Продажі не можуть мати від’ємне значення
                results.Add(
                    (float)Math.Round(
                        Math.Max(0, prediction.PredictedSales), 2));
            }

            return results;
        }
    }
}
