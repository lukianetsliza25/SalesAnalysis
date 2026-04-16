// SalesAnalysis.Core/Models/SalesDataPoint.cs
using Microsoft.ML.Data;
using System.ComponentModel.DataAnnotations.Schema;


[NotMapped]
public class SalesDataPoint
{
    [LoadColumn(0)]
    public float Year { get; set; } // Часовий індекс (1, 2, 3...)

    [LoadColumn(1), ColumnName("Label")]
    public float SalesAmount { get; set; } // Значення для прогнозування (Дохід)
}

[NotMapped]
public class SalesPrediction
{
    [ColumnName("Score")]
    public float PredictedSales { get; set; }
}