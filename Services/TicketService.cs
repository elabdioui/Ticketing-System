using Microsoft.EntityFrameworkCore;
using Ticketing_System.Models;
using Ticketing_System.Services;
using Ticketing_System; // adapte si nécessaire

namespace Ticketing_System.Services
{
    public class TicketService : ITicketService
    {
        private readonly ApplicationDbContext _context;

        public TicketService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Ticket>> GetAllTicketsAsync()
        {
            return await _context.Tickets
                .Include(t => t.CreatedByUser)
                .Include(t => t.AssignedToUser)
                .Include(t => t.AssignedToTeam)
                .ToListAsync();
        }

        public async Task<Ticket?> GetTicketByIdAsync(int id)
        {
            return await _context.Tickets
                .Include(t => t.CreatedByUser)
                .Include(t => t.AssignedToUser)
                .Include(t => t.AssignedToTeam)
                .FirstOrDefaultAsync(t => t.TicketID == id);
        }

        public async Task CreateTicketAsync(Ticket ticket)
        {
            _context.Tickets.Add(ticket);
            await _context.SaveChangesAsync();
        }

        public async Task<bool> UpdateTicketAsync(Ticket ticket)
        {
            var existing = await _context.Tickets.FindAsync(ticket.TicketID);
            if (existing == null) return false;

            _context.Entry(existing).CurrentValues.SetValues(ticket);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task DeleteTicketAsync(int id)
        {
            var ticket = await _context.Tickets.FindAsync(id);
            if (ticket != null)
            {
                _context.Tickets.Remove(ticket);
                await _context.SaveChangesAsync();
            }
        }
    }
}
