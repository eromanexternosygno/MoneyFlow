using DocumentFormat.OpenXml.Office.CustomUI;
using MoneyFlow.Context;
using MoneyFlow.DTOs;
using MoneyFlow.Entities;
using MoneyFlow.Interfaces;

namespace MoneyFlow.Managers;

public class TransactionManager : ITransactionManager
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<TransactionManager> _logger;
    public TransactionManager(AppDbContext dbContext, ILogger<TransactionManager> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }
    public int SaveNew(TransactionDTO transactionDTO)
    {
        var entity = new Transaction
        {
            UserId = transactionDTO.UserId,
            ServiceId = transactionDTO.ServiceId,
            TotalAmount = transactionDTO.TotalAmount,
            Date = transactionDTO.Date,
            Comment = transactionDTO.Comment
        };
        try
        {
            _dbContext.Transaction.Add(entity);
            int result = _dbContext.SaveChanges();
            // Log the successful transaction save (not implemented here)
            _logger.LogInformation("Transaction saved successfully for UserId: {UserId}, ServiceId: {ServiceId}, Amount: {Amount}", transactionDTO.UserId, transactionDTO.ServiceId, transactionDTO.TotalAmount);
            return result;
        }
        catch (Exception ex)
        {
            // logger error and return -1 to indicate failure
            _logger.LogError(ex, "Failed to save transaction for UserId: {UserId}, with Message: {Message}", transactionDTO.UserId, ex.Message);
            return -1;
        }
    }

    // Método para obtener el histórico de transacciones de un usuario específico
    public async Task<IEnumerable<HistoryTransactionDTO>> GetTransactionsHistory(DateOnly startDate, DateOnly endDate, int UserId)
    {
        try
        {
            var transactions = _dbContext.Transaction
            .Where(t => t.UserId == UserId &&
            t.Date >= startDate && t.Date <= endDate
            ).Select(item => new HistoryTransactionDTO
            {
                Date = item.Date.ToString("dd/MM/yyyy"),
                Month = item.Date.ToString("MMMM"),
                TypeService = item.Service.Type,
                Service = item.Service.Name,
                Amount = item.TotalAmount
            }).ToList();

            return transactions;
        }
        catch (Exception)
        {
            // Log the error (not implemented here)
            _logger.LogError("Failed to retrieve transaction history for UserId: {UserId}, with Message: {Message}", UserId, "An error occurred while fetching transaction history.");
            return Enumerable.Empty<HistoryTransactionDTO>();
        }
    }
}
