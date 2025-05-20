using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ticketing_System;
using Ticketing_System.Models;
using Ticketing_System.Repository.Interfaces;
using Ticketing_System.Repository_Pattern.Interfaces;
using Ticketing_System.Service_Layer.Interfaces;
using Ticketing_System.Service_Layer.Service;

public class TicketService : ITicketService
{
    private readonly ITicketRepository _ticketRepository;
    private readonly ITicketHistoryService _historyService;
    private readonly IUserRepository _userRepository;
    private readonly IAssignmentRuleService _assignmentRuleService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CompleteUserDeletionService> _logger;


    public TicketService(
        ITicketRepository ticketRepository,
        ITicketHistoryService historyService,
        IUserRepository userRepository,
        IAssignmentRuleService assignmentRuleService,
        ILogger<CompleteUserDeletionService> logger,
        ApplicationDbContext context)
    {
        _ticketRepository = ticketRepository;
        _historyService = historyService;
        _userRepository = userRepository;
        _assignmentRuleService = assignmentRuleService;
        _context = context;
        _logger = logger;
    }

    // Méthodes de base CRUD
    public async Task<IEnumerable<Ticket>> GetAllTicketsAsync()
    {
        return await _ticketRepository.GetAllAsync();
    }

    public async Task<Ticket> GetTicketByIdAsync(int ticketId)
    {
        var ticket = await _ticketRepository.GetByIdAsync(ticketId);
        if (ticket == null)
        {
            throw new KeyNotFoundException($"Ticket with ID {ticketId} not found");
        }
        return ticket;
    }

    public async Task<Ticket> CreateTicketAsync(Ticket ticket)
    {
        // Validation de base
        if (ticket == null) throw new ArgumentNullException(nameof(ticket));
        if (string.IsNullOrEmpty(ticket.Title)) throw new ArgumentException("Ticket title cannot be empty");
        if (string.IsNullOrEmpty(ticket.Description)) throw new ArgumentException("Ticket description cannot be empty");

        ticket.TicketComments = new List<TicketComment>();
        ticket.TicketAttachments = new List<Attachment>();

        // Valeurs par défaut
        ticket.CreatedDate = DateTime.Now;
        ticket.UpdatedDate = DateTime.Now;
        ticket.Status = TicketStatus.New; // Changed to New instead of Open
        if (string.IsNullOrEmpty(ticket.Source)) ticket.Source = "Web";

        // Créer le ticket
        var createdTicket = await _ticketRepository.AddAsync(ticket);

        // MODIFICATION ICI: S'assurer que l'ID utilisateur existe
        string changedByUserId = ticket.CreatedByUserId;
        var userExists = await _userRepository.UserExistsAsync(changedByUserId);

        if (!userExists)
        {
            // Utiliser un ID utilisateur système si l'utilisateur n'existe pas
            changedByUserId = "system"; // Assurez-vous que cet ID existe dans AspNetUsers
        }

        // Ajouter une entrée d'historique pour la création
        var history = new TicketHistory
        {
            TicketID = createdTicket.TicketID,
            ChangedByUserId = changedByUserId, // Utiliser l'ID validé
            FieldName = "Status",
            OldValue = "",
            NewValue = "Created",
            ChangedDate = DateTime.Now
        };

        await _historyService.AddHistoryEntryAsync(history);

        // NOUVELLE MODIFICATION: 
        // Appliquer les règles d'assignation automatiquement dès la création
        // Note: Cette partie est maintenant gérée dans le contrôleur pour éviter les dépendances circulaires
        // await _assignmentRuleService.ApplyRuleToTicketAsync(createdTicket.TicketID);

        return createdTicket;
    }

    public async Task UpdateTicketAsync(Ticket ticket)
    {
        if (ticket == null) throw new ArgumentNullException(nameof(ticket));

        var existingTicket = await _ticketRepository.GetByIdAsync(ticket.TicketID);
        if (existingTicket == null)
        {
            throw new KeyNotFoundException($"Ticket with ID {ticket.TicketID} not found");
        }

        // Création des entrées d'historique pour chaque champ modifié
        await TrackChanges(existingTicket, ticket, ticket.CreatedByUserId);

        // Mettre à jour les propriétés de l'entité existante au lieu de créer une nouvelle instance
        existingTicket.Title = ticket.Title;
        existingTicket.Description = ticket.Description;
        existingTicket.Category = ticket.Category;
        existingTicket.Priority = ticket.Priority;
        existingTicket.Status = ticket.Status;
        existingTicket.AssignedToUserId = ticket.AssignedToUserId;
        existingTicket.AssignedToTeamID = ticket.AssignedToTeamID;
        existingTicket.DueDate = ticket.DueDate;
        existingTicket.Source = ticket.Source;

        // Mise à jour de la date de modification
        existingTicket.UpdatedDate = DateTime.Now;

        // Si le statut est résolu et que la date de résolution n'est pas définie, la définir
        if (existingTicket.Status == TicketStatus.Resolved && !existingTicket.ResolutionDate.HasValue)
        {
            existingTicket.ResolutionDate = DateTime.Now;
        }
        // Si le statut est fermé et que la date de fermeture n'est pas définie, la définir
        else if (existingTicket.Status == TicketStatus.Closed && !existingTicket.ClosedDate.HasValue)
        {
            existingTicket.ClosedDate = DateTime.Now;
        }

        // Mise à jour du ticket
        await _ticketRepository.UpdateAsync(existingTicket);
    }

    // TicketService.cs - Fixed DeleteTicketAsync method
    public async Task DeleteTicketAsync(int ticketId)
    {
        var ticket = await _ticketRepository.GetByIdAsync(ticketId);
        if (ticket == null)
        {
            throw new KeyNotFoundException($"Ticket with ID {ticketId} not found");
        }

        // Utiliser le DbContext via le repository pour accéder à la stratégie d'exécution
        var repository = _ticketRepository as TicketRepository;
        if (repository == null)
        {
            throw new Exception("Le repository utilisé n'est pas du type TicketRepository.");
        }

        var context = repository.GetContext();
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                await _ticketRepository.DeleteAsync(ticket);
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Failed to delete ticket: {ex.Message}", ex);
            }
        });
    }

    // Filtrage et recherche
    public async Task<IEnumerable<Ticket>> GetTicketsByUserIdAsync(string userId)
    {
        return await _ticketRepository.GetTicketsByUserIdAsync(userId);
    }

    public async Task<IEnumerable<Ticket>> GetTicketsByAssignedUserIdAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            throw new ArgumentNullException(nameof(userId), "L'ID utilisateur ne peut pas être null ou vide.");
        }

        // Vérifier si l'utilisateur existe
        var user = await _userRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            throw new KeyNotFoundException($"Utilisateur avec ID {userId} non trouvé");
        }

        return await _ticketRepository.GetTicketsByAssignedUserIdAsync(userId);
    }

    public async Task<IEnumerable<Ticket>> GetTicketsByAssignedTeamIdAsync(int teamId)
    {
        return await _ticketRepository.GetTicketsByAssignedTeamIdAsync(teamId);
    }

    public async Task<IEnumerable<Ticket>> GetTicketsByStatusAsync(TicketStatus status)
    {
        return await _ticketRepository.GetTicketsByStatusAsync(status);
    }

    public async Task<IEnumerable<Ticket>> GetTicketsByPriorityAsync(TicketPriority priority)
    {
        return await _ticketRepository.GetTicketsByPriorityAsync(priority);
    }

    public async Task<IEnumerable<Ticket>> GetTicketsByCategoryAsync(TicketCategory category)
    {
        return await _ticketRepository.GetTicketsByCategoryAsync(category);
    }

    public async Task<IEnumerable<Ticket>> SearchTicketsAsync(string searchTerm)
    {
        return await _ticketRepository.SearchTicketsAsync(searchTerm);
    }

    public async Task<Ticket> ChangeTicketStatusAsync(int ticketId, TicketStatus newStatus, string userId)
    {
        var ticket = await _ticketRepository.GetByIdAsync(ticketId);
        if (ticket == null)
        {
            throw new KeyNotFoundException($"Ticket with ID {ticketId} not found");
        }

        var oldStatus = ticket.Status;

        // Mise à jour du statut
        ticket.Status = newStatus;
        ticket.UpdatedDate = DateTime.Now;

        // Mise à jour de dates spécifiques selon le statut
        if (newStatus == TicketStatus.Resolved && !ticket.ResolutionDate.HasValue)
        {
            ticket.ResolutionDate = DateTime.Now;
        }
        else if (newStatus == TicketStatus.Closed && !ticket.ClosedDate.HasValue)
        {
            ticket.ClosedDate = DateTime.Now;
        }

        await _ticketRepository.UpdateAsync(ticket);

        // Ajout d'une entrée dans l'historique
        await _historyService.AddHistoryEntryAsync(new TicketHistory
        {
            TicketID = ticketId,
            ChangedByUserId = userId,
            FieldName = "Status",
            OldValue = oldStatus.ToString(),
            NewValue = newStatus.ToString(),
            ChangedDate = DateTime.Now
        });

        return ticket;
    }

    public async Task<Ticket> AssignTicketAsync(int ticketId, string assignedToUserId, string updatedByUserId)
    {
        var ticket = await _ticketRepository.GetByIdAsync(ticketId);
        if (ticket == null)
        {
            throw new KeyNotFoundException($"Ticket with ID {ticketId} not found");
        }

        var oldAssignedUser = ticket.AssignedToUserId;
        var oldAssignedTeamId = ticket.AssignedToTeamID;

        ticket.AssignedToUserId = assignedToUserId;

        // MODIFICATION: Au lieu de mettre AssignedToTeamID à null, trouver l'équipe de l'agent
        if (!string.IsNullOrEmpty(assignedToUserId))
        {
            // Rechercher l'équipe de l'agent
            var teamMember = await _context.TeamMembers
                .FirstOrDefaultAsync(tm => tm.UserId == assignedToUserId);

            if (teamMember != null)
            {
                ticket.AssignedToTeamID = teamMember.TeamID;
            }
            else
            {
                ticket.AssignedToTeamID = null;
            }
        }
        else
        {
            ticket.AssignedToTeamID = null;
        }

        ticket.UpdatedDate = DateTime.Now;

        await _ticketRepository.UpdateAsync(ticket);

        // Ajout d'une entrée dans l'historique pour l'utilisateur
        await _historyService.AddHistoryEntryAsync(new TicketHistory
        {
            TicketID = ticketId,
            ChangedByUserId = updatedByUserId,
            FieldName = "AssignedToUser",
            OldValue = oldAssignedUser ?? "Unassigned",
            NewValue = assignedToUserId ?? "Unassigned",
            ChangedDate = DateTime.Now
        });

        // Ajout d'une entrée dans l'historique pour l'équipe si elle a changé
        if (oldAssignedTeamId != ticket.AssignedToTeamID)
        {
            await _historyService.AddHistoryEntryAsync(new TicketHistory
            {
                TicketID = ticketId,
                ChangedByUserId = updatedByUserId,
                FieldName = "AssignedToTeam",
                OldValue = oldAssignedTeamId?.ToString() ?? "Unassigned",
                NewValue = ticket.AssignedToTeamID?.ToString() ?? "Unassigned",
                ChangedDate = DateTime.Now
            });
        }

        return ticket;
    }

    public async Task<Ticket> AssignTicketToTeamAsync(int ticketId, int teamId, string updatedByUserId)
    {
        var ticket = await _ticketRepository.GetByIdAsync(ticketId);
        if (ticket == null)
        {
            throw new KeyNotFoundException($"Ticket with ID {ticketId} not found");
        }

        var oldTeamId = ticket.AssignedToTeamID;
        ticket.AssignedToTeamID = teamId;
        ticket.AssignedToUserId = null; // Désassigner de l'utilisateur si assigné à une équipe
        ticket.UpdatedDate = DateTime.Now;

        await _ticketRepository.UpdateAsync(ticket);

        // Ajout d'une entrée dans l'historique
        await _historyService.AddHistoryEntryAsync(new TicketHistory
        {
            TicketID = ticketId,
            ChangedByUserId = updatedByUserId,
            FieldName = "AssignedToTeam",
            OldValue = oldTeamId?.ToString() ?? "Unassigned",
            NewValue = teamId.ToString(),
            ChangedDate = DateTime.Now
        });

        return ticket;
    }

    public async Task<int> GetTicketCountByStatusAsync(TicketStatus status)
    {
        return await _ticketRepository.GetTicketCountByStatusAsync(status);
    }

    public async Task<Dictionary<TicketStatus, int>> GetTicketStatusDistributionAsync()
    {
        var result = new Dictionary<TicketStatus, int>();

        // Obtenir tous les statuts possibles (à partir de l'énumération)
        var statuses = Enum.GetValues(typeof(TicketStatus)).Cast<TicketStatus>();

        // Pour chaque statut, obtenir le nombre de tickets
        foreach (var status in statuses)
        {
            int count = await _ticketRepository.GetTicketCountByStatusAsync(status);
            result.Add(status, count);
        }

        return result;
    }

    public async Task<Dictionary<TicketPriority, int>> GetTicketPriorityDistributionAsync()
    {
        var result = new Dictionary<TicketPriority, int>();
        var allTickets = await _ticketRepository.GetAllAsync();

        // Obtenir tous les niveaux de priorité possibles
        var priorities = Enum.GetValues(typeof(TicketPriority)).Cast<TicketPriority>();

        // Pour chaque priorité, compter les tickets
        foreach (var priority in priorities)
        {
            int count = allTickets.Count(t => t.Priority == priority);
            result.Add(priority, count);
        }

        return result;
    }

    private async Task TrackChanges(Ticket oldTicket, Ticket newTicket, string userId)
    {
        // Vérification du titre
        if (oldTicket.Title != newTicket.Title)
        {
            await _historyService.AddHistoryEntryAsync(new TicketHistory
            {
                TicketID = oldTicket.TicketID,
                ChangedByUserId = userId,
                FieldName = "Title",
                OldValue = oldTicket.Title,
                NewValue = newTicket.Title,
                ChangedDate = DateTime.Now
            });
        }

        // Vérification de la description
        if (oldTicket.Description != newTicket.Description)
        {
            await _historyService.AddHistoryEntryAsync(new TicketHistory
            {
                TicketID = oldTicket.TicketID,
                ChangedByUserId = userId,
                FieldName = "Description",
                OldValue = oldTicket.Description,
                NewValue = newTicket.Description,
                ChangedDate = DateTime.Now
            });
        }

        // Vérification de la catégorie
        if (oldTicket.Category != newTicket.Category)
        {
            await _historyService.AddHistoryEntryAsync(new TicketHistory
            {
                TicketID = oldTicket.TicketID,
                ChangedByUserId = userId,
                FieldName = "Category",
                OldValue = oldTicket.Category.ToString(),
                NewValue = newTicket.Category.ToString(),
                ChangedDate = DateTime.Now
            });
        }

        // Vérification de la priorité
        if (oldTicket.Priority != newTicket.Priority)
        {
            await _historyService.AddHistoryEntryAsync(new TicketHistory
            {
                TicketID = oldTicket.TicketID,
                ChangedByUserId = userId,
                FieldName = "Priority",
                OldValue = oldTicket.Priority.ToString(),
                NewValue = newTicket.Priority.ToString(),
                ChangedDate = DateTime.Now
            });
        }

        // Vérification du statut
        if (oldTicket.Status != newTicket.Status)
        {
            await _historyService.AddHistoryEntryAsync(new TicketHistory
            {
                TicketID = oldTicket.TicketID,
                ChangedByUserId = userId,
                FieldName = "Status",
                OldValue = oldTicket.Status.ToString(),
                NewValue = newTicket.Status.ToString(),
                ChangedDate = DateTime.Now
            });
        }

        // Vérification de l'assignation à un utilisateur
        if (oldTicket.AssignedToUserId != newTicket.AssignedToUserId)
        {
            await _historyService.AddHistoryEntryAsync(new TicketHistory
            {
                TicketID = oldTicket.TicketID,
                ChangedByUserId = userId,
                FieldName = "AssignedToUser",
                OldValue = oldTicket.AssignedToUserId ?? "Unassigned",
                NewValue = newTicket.AssignedToUserId ?? "Unassigned",
                ChangedDate = DateTime.Now
            });
        }

        // Vérification de l'assignation à une équipe
        if (oldTicket.AssignedToTeamID != newTicket.AssignedToTeamID)
        {
            await _historyService.AddHistoryEntryAsync(new TicketHistory
            {
                TicketID = oldTicket.TicketID,
                ChangedByUserId = userId,
                FieldName = "AssignedToTeam",
                OldValue = oldTicket.AssignedToTeamID?.ToString() ?? "Unassigned",
                NewValue = newTicket.AssignedToTeamID?.ToString() ?? "Unassigned",
                ChangedDate = DateTime.Now
            });
        }

        // Vérification de la date d'échéance
        if (oldTicket.DueDate != newTicket.DueDate)
        {
            await _historyService.AddHistoryEntryAsync(new TicketHistory
            {
                TicketID = oldTicket.TicketID,
                ChangedByUserId = userId,
                FieldName = "DueDate",
                OldValue = oldTicket.DueDate?.ToString("dd/MM/yyyy") ?? "Not set",
                NewValue = newTicket.DueDate?.ToString("dd/MM/yyyy") ?? "Not set",
                ChangedDate = DateTime.Now
            });
        }
    }
}