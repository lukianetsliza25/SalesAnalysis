// SalesAnalysis.Data/Services/ImportService.cs
using CsvHelper.Configuration;
using CsvHelper;
using SalesAnalysis.Core.Entities;
using System.Globalization;
using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CsvHelper.TypeConversion;

namespace SalesAnalysis.Data.Services
{
    // Клас ImportService (повний код для контексту)
    public class ImportService
    {
        private readonly IServiceProvider _serviceProvider;

        public ImportService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<int> ImportTransactionsFromCsvAsync(Stream fileStream)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

                await context.Database.EnsureCreatedAsync();

                using var reader = new StreamReader(fileStream);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

                csv.Context.RegisterClassMap<TransactionMap>();

                try
                {
                    var transactions = csv.GetRecords<Transaction>().ToList();

                    await context.Transactions.AddRangeAsync(transactions);
                    int importedCount = await context.SaveChangesAsync();

                    return importedCount;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Помилка під час парсингу або збереження даних. Перевірте формат CSV та назви колонок (InvoiceDate, CustomerID, StockCode, Quantity, UnitPrice).", ex);
                }
            }
        }
    }

    // -----------------------------------------------------
    // Конвертер для автоматичного розрахунку доходу (Revenue)
    // Revenue = Quantity × UnitPrice
    // Використовується під час імпорту CSV-файлу
    public class RevenueConverter : DefaultTypeConverter
    {
        // Метод викликається CsvHelper під час зчитування значення з CSV
        public override object ConvertFromString(
            string text,
            IReaderRow row,
            MemberMapData memberMapData)
        {
            // Отримання значень кількості та ціни за одиницю
            // безпосередньо з рядка CSV-файлу
            var quantityString = row.GetField<string>("Quantity");
            var unitPriceString = row.GetField<string>("UnitPrice");

            // Безпечний парсинг числових значень
            if (int.TryParse(quantityString, out int quantity) &&
                decimal.TryParse(
                    unitPriceString,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimal unitPrice))
            {
                // Обчислення доходу як добутку кількості та ціни
                return quantity * unitPrice;
            }

            // Якщо парсинг не вдався — повертаємо 0,
            // що запобігає аварійному завершенню імпорту
            return 0m;
        }
    }

    // -----------------------------------------------------
    // Клас мапінгу CSV-колонок на поля сутності Transaction
    // Забезпечує коректне зчитування та підготовку даних
    public sealed class TransactionMap : ClassMap<Transaction>
    {
        public TransactionMap()
        {
            // 1. Мапінг дати транзакції
            Map(m => m.Date).Name("InvoiceDate");

            // 2. Мапінг ідентифікатора клієнта
            Map(m => m.CustomerId).Name("CustomerID");

            // 3. Мапінг ідентифікатора товару
            Map(m => m.ProductId).Name("StockCode");

            // 4. Мапінг кількості придбаних одиниць
            Map(m => m.Quantity).Name("Quantity");

            // 5. Мапінг ціни за одиницю товару
            Map(m => m.UnitPrice).Name("UnitPrice");

            // 6. Обчислення доходу через власний конвертер
            // Значення Revenue не зчитується напряму з CSV,
            // а обчислюється автоматично під час імпорту
            Map(m => m.Revenue)
                .Name("UnitPrice")
                .TypeConverter<RevenueConverter>();
        }
    }

}