using Ticketing_System.Models;
using Ticketing_System;
using Microsoft.EntityFrameworkCore;
using Ticketing_System.Repository.Interfaces;

public class TicketRepository : Repository<Ticket>, ITicketRepository
{
    public TicketRepository(ApplicationDbContext context) : base(context) { }

    public ApplicationDbContext GetContext()
    {
        return _context;
    }

    public async Task<IEnumerable<Ticket>> GetTicketsByUserIdAsync(string userId)
    {
        return await _dbSet
            .Where(t => t.CreatedByUserId == userId)
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.AssignedToTeam)
            .ToListAsync();
    }

    public async Task<IEnumerable<Ticket>> GetTicketsByAssignedUserIdAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return new List<Ticket>();
        }

        return await _dbSet
            .Where(t => t.AssignedToUserId == userId)
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.AssignedToTeam)
            .ToListAsync();
    }

    public async Task<IEnumerable<Ticket>> GetTicketsByAssignedTeamIdAsync(int teamId)
    {
        return await _dbSet
            .Where(t => t.AssignedToTeamID == teamId)
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToTeam)
            .ToListAsync();
    }

    public async Task<IEnumerable<Ticket>> GetTicketsByStatusAsync(TicketStatus status)
    {
        return await _dbSet
            .Where(t => t.Status == status)
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.AssignedToTeam)
            .ToListAsync();
    }

    public async Task<IEnumerable<Ticket>> GetTicketsByPriorityAsync(TicketPriority priority)
    {
        return await _dbSet
            .Where(t => t.Priority == priority)
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.AssignedToTeam)
            .ToListAsync();
    }

    public async Task<IEnumerable<Ticket>> GetTicketsByCategoryAsync(TicketCategory category)
    {
        return await _dbSet
            .Where(t => t.Category == category)
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.AssignedToTeam)
            .ToListAsync();
    }

    public async Task<IEnumerable<Ticket>> SearchTicketsAsync(string searchTerm)
    {
        if (string.IsNullOrEmpty(searchTerm))
            return new List<Ticket>();

        return await _dbSet
            .Where(t => t.Title.Contains(searchTerm) || t.Description.Contains(searchTerm))
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.AssignedToTeam)
            .ToListAsync();
    }

    
    public async Task<int> GetTicketCountByStatusAsync(TicketStatus status)
    {
        return await _dbSet.CountAsync(t => t.Status == status);
    }

    public override async Task<Ticket> GetByIdAsync(int id)
    {
        return await _dbSet
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.AssignedToTeam)
            .Include(t => t.TicketComments)
            .ThenInclude(c => c.User)
            .Include(t => t.TicketAttachments)
            .FirstOrDefaultAsync(t => t.TicketID == id);
    }

    public IQueryable<Ticket> Query()
{
    return _context.Tickets;
}

}