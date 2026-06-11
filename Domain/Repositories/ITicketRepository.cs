using Domain.Tickets;

namespace Domain.Repositories;

public interface ITicketRepository
{
    Task AddAsync(ErrorTicket ticket);
    Task<ErrorTicket?> GetByIdAsync(Guid id);
    Task<IList<ErrorTicket>> GetAllAsync();
    Task UpdateAsync(ErrorTicket ticket);
}
