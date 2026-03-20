using MoneyFlow.DTOs;

namespace MoneyFlow.Interfaces
{
    public interface ITransactionManager
    {
        int SaveNew(TransactionDTO transactionDTO);
        // Método para obtener el histórico de transacciones de un usuario específico
        Task<IEnumerable<HistoryTransactionDTO>> GetTransactionsHistory(DateOnly startDate, DateOnly endDate, int UserId);
    }
}
