using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketingSystem.Data;
using TicketingSystem.Models;
using TicketingSystem.Services.Interfaces;
using TicketingSystem.ViewModels;

namespace TicketingSystem.Controllers
{
    [Authorize]
    public class TicketsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ITicketService _ticketService;
        private readonly INotificationService _notificationService;

        public TicketsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ITicketService ticketService,
            INotificationService notificationService)
        {
            _context = context;
            _userManager = userManager;
            _ticketService = ticketService;
            _notificationService = notificationService;
        }

        // GET: /Tickets
        public async Task<IActionResult> Index(string searchString, int? categoryId, int? statusId, int? priorityId, int page = 1)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var isAgent = User.IsInRole("Agent") || User.IsInRole("Manager") || User.IsInRole("Admin");
            
            var ticketsQuery = _context.Tickets
                .Include(t => t.Category)
                .Include(t => t.Priority)
                .Include(t => t.Status)
                .Include(t => t.CreatedBy)
                .Include(t => t.AssignedTo)
                .AsQueryable();

            // Si l'utilisateur n'est pas un agent, ne montrer que ses propres tickets
            if (!isAgent)
            {
                ticketsQuery = ticketsQuery.Where(t => t.CreatedByID == currentUser.Id);
            }

            // Appliquer les filtres
            if (!string.IsNullOrEmpty(searchString))
            {
                ticketsQuery = ticketsQuery.Where(t => t.Title.Contains(searchString) 
                                                 || t.Description.Contains(searchString)
                                                 || t.TicketID.ToString().Contains(searchString));
            }

            if (categoryId.HasValue)
            {
                ticketsQuery = ticketsQuery.Where(t => t.CategoryID == categoryId.Value);
            }

            if (statusId.HasValue)
            {
                ticketsQuery = ticketsQuery.Where(t => t.StatusID == statusId.Value);
            }

            if (priorityId.HasValue)
            {
                ticketsQuery = ticketsQuery.Where(t => t.PriorityID == priorityId.Value);
            }

            // Trier par date de création (plus récent en premier)
            ticketsQuery = ticketsQuery.OrderByDescending(t => t.CreatedDate);

            // Pagination
            const int pageSize = 10;
            var tickets = await ticketsQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            var totalItems = await ticketsQuery.CountAsync();

            // Préparer les listes déroulantes pour les filtres
            ViewBag.Categories = new SelectList(_context.TicketCategories, "CategoryID", "CategoryName");
            ViewBag.Statuses = new SelectList(_context.TicketStatuses, "StatusID", "StatusName");
            ViewBag.Priorities = new SelectList(_context.TicketPriorities, "PriorityID", "PriorityName");
            
            var model = new TicketListViewModel
            {
                Tickets = tickets,
                PageInfo = new PageInfo
                {
                    CurrentPage = page,
                    ItemsPerPage = pageSize,
                    TotalItems = totalItems
                },
                SearchString = searchString,
                CategoryId = categoryId,
                StatusId = statusId,
                PriorityId = priorityId
            };

            return View(model);
        }

        // GET: /Tickets/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var ticket = await _context.Tickets
                .Include(t => t.Category)
                .Include(t => t.Priority)
                .Include(t => t.Status)
                .Include(t => t.CreatedBy)
                .Include(t => t.AssignedTo)
                .Include(t => t.AssignedTeam)
                .Include(t => t.Comments)
                    .ThenInclude(c => c.User)
                .Include(t => t.Attachments)
                .FirstOrDefaultAsync(m => m.TicketID == id);

            if (ticket == null)
            {
                return NotFound();
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var isAgent = User.IsInRole("Agent") || User.IsInRole("Manager") || User.IsInRole("Admin");
            
            // Vérifier si l'utilisateur a accès à ce ticket
            if (!isAgent && ticket.CreatedByID != currentUser.Id)
            {
                return Forbid();
            }

            var model = new TicketDetailsViewModel
            {
                Ticket = ticket,
                NewComment = new TicketCommentViewModel
                {
                    TicketID = ticket.TicketID
                },
                TicketHistory = await _context.TicketHistory
                    .Where(h => h.TicketID == id)
                    .Include(h => h.ChangedBy)
                    .OrderByDescending(h => h.ChangedDate)
                    .ToListAsync()
            };

            return View(model);
        }

        // GET: /Tickets/Create
        public IActionResult Create()
        {
            ViewBag.Categories = new SelectList(_context.TicketCategories, "CategoryID", "CategoryName");
            ViewBag.Priorities = new SelectList(_context.TicketPriorities, "PriorityID", "PriorityName");
            
            return View();
        }

        // POST: /Tickets/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateTicketViewModel model)
        {
            if (ModelState.IsValid)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                
                // Créer un nouveau ticket
                var ticket = new Ticket
                {
                    Title = model.Title,
                    Description = model.Description,
                    CategoryID = model.CategoryID,
                    PriorityID = model.PriorityID,
                    StatusID = _context.TicketStatuses.First(s => s.StatusName == "New").StatusID,
                    CreatedByID = currentUser.Id,
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now,
                    Source = "Web"
                };

                _context.Add(ticket);
                await _context.SaveChangesAsync();

                // Appliquer les règles d'attribution automatique
                await _ticketService.ApplyAssignmentRulesAsync(ticket.TicketID);

                // Ajouter les pièces jointes
                if (model.Attachments != null && model.Attachments.Count > 0)
                {
                    await _ticketService.AddAttachmentsAsync(ticket.TicketID, model.Attachments, currentUser.Id);
                }

                TempData["SuccessMessage"] = $"Ticket #{ticket.TicketID} créé avec succès.";
                return RedirectToAction(nameof(Details), new { id = ticket.TicketID });
            }

            ViewBag.Categories = new SelectList(_context.TicketCategories, "CategoryID", "CategoryName", model.CategoryID);
            ViewBag.Priorities = new SelectList(_context.TicketPriorities, "PriorityID", "PriorityName", model.PriorityID);
            
            return View(model);
        }

        // POST: /Tickets/AddComment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddComment(TicketCommentViewModel model)
        {
            if (ModelState.IsValid)
            {
                var ticket = await _context.Tickets.FindAsync(model.TicketID);
                if (ticket == null)
                {
                    return NotFound();
                }

                var currentUser = await _userManager.GetUserAsync(User);
                var isAgent = User.IsInRole("Agent") || User.IsInRole("Manager") || User.IsInRole("Admin");
                
                // Vérifier si l'utilisateur a accès à ce ticket
                if (!isAgent && ticket.CreatedByID != currentUser.Id)
                {
                    return Forbid();
                }

                // Créer le commentaire
                var comment = new TicketComment
                {
                    TicketID = model.TicketID,
                    UserID = currentUser.Id,
                    CommentText = model.CommentText,
                    CommentDate = DateTime.Now,
                    IsInternal = model.IsInternal && isAgent // Seuls les agents peuvent créer des notes internes
                };

                _context.TicketComments.Add(comment);
                
                // Mettre à jour la date de mise à jour du ticket
                ticket.UpdatedDate = DateTime.Now;
                
                await _context.SaveChangesAsync();

                // Ajouter les pièces jointes
                if (model.Attachments != null && model.Attachments.Count > 0)
                {
                    await _ticketService.AddAttachmentsAsync(ticket.TicketID, model.Attachments, currentUser.Id, comment.CommentID);
                }

                // Envoyer des notifications
                await _notificationService.NotifyTicketCommentAsync(comment.CommentID);

                TempData["SuccessMessage"] = "Commentaire ajouté avec succès.";
                return RedirectToAction(nameof(Details), new { id = model.TicketID });
            }

            // Si on arrive ici, c'est qu'il y a une erreur, donc on redirige vers les détails du ticket
            return RedirectToAction(nameof(Details), new { id = model.TicketID });
        }

        // POST: /Tickets/UpdateStatus
        [HttpPost]
        [Authorize(Roles = "Agent,Manager,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int ticketId, int statusId)
        {
            var ticket = await _context.Tickets.FindAsync(ticketId);
            if (ticket == null)
            {
                return NotFound();
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var oldStatusId = ticket.StatusID;

            // Mettre à jour le statut
            await _ticketService.UpdateTicketStatusAsync(ticketId, statusId, currentUser.Id);

            // Vérifier si le ticket est résolu ou fermé
            var newStatus = await _context.TicketStatuses.FindAsync(statusId);
            if (newStatus.StatusName == "Resolved")
            {
                ticket.ResolutionDate = DateTime.Now;
            }
            else if (newStatus.IsClosedStatus)
            {
                ticket.ClosedDate = DateTime.Now;
            }

            ticket.UpdatedDate = DateTime.Now;
            await _context.SaveChangesAsync();

            // Créer une entrée dans l'historique
            var oldStatus = await _context.TicketStatuses.FindAsync(oldStatusId);
            _context.TicketHistory.Add(new TicketHistory
            {
                TicketID = ticketId,
                FieldName = "Status",
                OldValue = oldStatus.StatusName,
                NewValue = newStatus.StatusName,
                ChangedByID = currentUser.Id,
                ChangedDate = DateTime.Now
            });
            await _context.SaveChangesAsync();

            // Envoyer des notifications
            await _notificationService.NotifyStatusChangeAsync(ticketId, oldStatusId, statusId);

            TempData["SuccessMessage"] = $"Statut du ticket mis à jour: {newStatus.StatusName}";
            return RedirectToAction(nameof(Details), new { id = ticketId });
        }

        // POST: /Tickets/Assign
        [HttpPost]
        [Authorize(Roles = "Agent,Manager,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(int ticketId, string userId)
        {
            var ticket = await _context.Tickets.FindAsync(ticketId);
            if (ticket == null)
            {
                return NotFound();
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var oldAssignedUserId = ticket.AssignedToID;

            // Mettre à jour l'assignation
            ticket.AssignedToID = userId;
            ticket.AssignedTeamID = null; // Enlever l'assignation d'équipe si un utilisateur est assigné
            ticket.UpdatedDate = DateTime.Now;
            
            // Si le ticket est nouveau, le passer à "En cours"
            var newStatus = await _context.TicketStatuses.FirstAsync(s => s.StatusName == "New");
            var inProgressStatus = await _context.TicketStatuses.FirstAsync(s => s.StatusName == "In Progress");
            
            if (ticket.StatusID == newStatus.StatusID)
            {
                ticket.StatusID = inProgressStatus.StatusID;
                
                // Ajouter l'historique du changement de statut
                _context.TicketHistory.Add(new TicketHistory
                {
                    TicketID = ticketId,
                    FieldName = "Status",
                    OldValue = newStatus.StatusName,
                    NewValue = inProgressStatus.StatusName,
                    ChangedByID = currentUser.Id,
                    ChangedDate = DateTime.Now
                });
            }
            
            await _context.SaveChangesAsync();

            // Créer une entrée dans l'historique pour l'assignation
            string oldValue = oldAssignedUserId != null 
                ? (await _userManager.FindByIdAsync(oldAssignedUserId)).UserName 
                : "Non assigné";
                
            string newValue = userId != null 
                ? (await _userManager.FindByIdAsync(userId)).UserName 
                : "Non assigné";
                
            _context.TicketHistory.Add(new TicketHistory
            {
                TicketID = ticketId,
                FieldName = "AssignedTo",
                OldValue = oldValue,
                NewValue = newValue,
                ChangedByID = currentUser.Id,
                ChangedDate = DateTime.Now
            });
            await _context.SaveChangesAsync();

            // Envoyer des notifications
            await _notificationService.NotifyTicketAssignmentAsync(ticketId, userId);

            TempData["SuccessMessage"] = $"Ticket assigné à {newValue}";
            return RedirectToAction(nameof(Details), new { id = ticketId });
        }

        // Dashboard des tickets pour les agents
        [Authorize(Roles = "Agent,Manager,Admin")]
        public async Task<IActionResult> Dashboard()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            
            var viewModel = new TicketDashboardViewModel
            {
                NewTickets = await _context.Tickets
                    .Where(t => t.StatusID == _context.TicketStatuses.First(s => s.StatusName == "New").StatusID)
                    .CountAsync(),
                    
                MyTickets = await _context.Tickets
                    .Where(t => t.AssignedToID == currentUser.Id && 
                           !_context.TicketStatuses
                               .Where(s => s.IsClosedStatus)
                               .Select(s => s.StatusID)
                               .Contains(t.StatusID))
                    .CountAsync(),
                    
                OverdueTickets = await _context.Tickets
                    .Where(t => t.DueDate.HasValue && 
                           t.DueDate < DateTime.Now &&
                           !_context.TicketStatuses
                               .Where(s => s.IsClosedStatus)
                               .Select(s => s.StatusID)
                               .Contains(t.StatusID))
                    .CountAsync(),
                    
                RecentTickets = await _context.Tickets
                    .Include(t => t.Category)
                    .Include(t => t.Priority)
                    .Include(t => t.Status)
                    .Include(t => t.CreatedBy)
                    .OrderByDescending(t => t.CreatedDate)
                    .Take(5)
                    .ToListAsync(),
                    
                TicketsByStatus = await _context.Tickets
                    .GroupBy(t => t.Status.StatusName)
                    .Select(g => new ChartDataPoint { Label = g.Key, Value = g.Count() })
                    .ToListAsync(),
                    
                TicketsByPriority = await _context.Tickets
                    .GroupBy(t => t.Priority.PriorityName)
                    .Select(g => new ChartDataPoint { Label = g.Key, Value = g.Count() })
                    .ToListAsync()
            };
            
            return View(viewModel);
        }
    }
}
