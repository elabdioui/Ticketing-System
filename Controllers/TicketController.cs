using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Ticketing_System.Models;
using Ticketing_System.Service_Layer;
using Ticketing_System.Service_Layer.Interfaces;

namespace Ticketing_System.Controllers
{
    using global::Ticketing_System.Repository.Interfaces;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Rendering;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Claims;
    using System.Threading.Tasks;


    namespace Ticketing_System.Controllers
    {
        public class TicketController : Controller
        {
            private readonly ITicketService _ticketService;
            private readonly ITicketCommentService _commentService;
            private readonly ITicketHistoryService _historyService;
            private readonly IAttachmentService _attachmentService;
            private readonly UserManager<User> _userManager;
            private readonly INotificationService _notificationService;
            private readonly IAssignmentRuleService _assignmentRuleService;
            private readonly IEscalationRuleService _escalationRuleService;
            private readonly ISupportTeamService _supportTeamService;
            private readonly ITicketRepository _ticketRepository;
            private readonly ApplicationDbContext _context;
            private readonly ILogger<TicketController> _logger;

            public TicketController(
                ITicketService ticketService,
                ITicketCommentService commentService,
                ITicketHistoryService historyService,
                IAttachmentService attachmentService,
                UserManager<User> userManager,
                IAssignmentRuleService assignmentRuleService,
                IEscalationRuleService escalationRuleService,
                ISupportTeamService supportTeamService,
                ILogger<TicketController> logger,
                ITicketRepository ticketRepository,
                ApplicationDbContext context,
                INotificationService notificationService)
            {
                _ticketService = ticketService;
                _commentService = commentService;
                _historyService = historyService;
                _attachmentService = attachmentService;
                _userManager = userManager;
                _assignmentRuleService = assignmentRuleService;
                _escalationRuleService = escalationRuleService;
                _supportTeamService = supportTeamService;
                _logger = logger;
                _notificationService = notificationService;
                _ticketRepository = ticketRepository;
                _context = context;
            }

            // GET: Ticket
            [HttpGet]
            [Authorize]
            public async Task<IActionResult> Index()
            {
                var tickets = await _ticketService.GetAllTicketsAsync();
                return View(tickets);
            }

            // GET: Ticket/MyTickets
            [HttpGet]
            [Authorize]
            public async Task<IActionResult> MyTickets()
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var tickets = await _ticketService.GetTicketsByUserIdAsync(userId);
                return View(tickets);
            }

            // GET: Ticket/AssignedTickets
            [HttpGet]
            [Authorize(Roles = "Admin,SupportAgent")]
            public async Task<IActionResult> AssignedTickets()
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized();
                }

                _logger.LogInformation($"Récupération des tickets assignés pour l'utilisateur ID: {userId}");
                var tickets = await _ticketService.GetTicketsByAssignedUserIdAsync(userId);
                _logger.LogInformation($"Nombre de tickets assignés trouvés: {tickets.Count()}");

                return View(tickets);
            }

            // GET: Ticket/Details/5
            [HttpGet]
            public async Task<IActionResult> Details(int id)
            {
                try
                {
                    var ticket = await _ticketService.GetTicketByIdAsync(id);
                    var history = await _historyService.GetHistoryByTicketIdAsync(id);
                    var comments = await _commentService.GetCommentsByTicketIdAsync(id);
                    var attachments = await _attachmentService.GetAttachmentsByTicketIdAsync(id);

                    // Récupérer tous les IDs d'utilisateurs uniques des historiques
                    var historyUserIds = history.Select(h => h.ChangedByUserId).Distinct().ToList();

                    // Charger les utilisateurs correspondants
                    Dictionary<string, User> users = new Dictionary<string, User>();
                    foreach (var userId in historyUserIds)
                    {
                        var user = await _userManager.FindByIdAsync(userId);
                        if (user != null)
                        {
                            users[userId] = user;
                        }
                    }

                    ViewBag.History = history;
                    ViewBag.HistoryUsers = users;
                    ViewBag.Comments = comments;
                    ViewBag.Attachments = attachments;

                    // Check if the ticket is eligible for escalation
                    if (User.IsInRole("Admin") || User.IsInRole("SupportAgent"))
                    {
                        var ticketsNeedingEscalation = await _escalationRuleService.GetTicketsNeedingEscalationAsync();
                        ViewBag.NeedsEscalation = ticketsNeedingEscalation.Any(t => t.TicketID == id);
                    }

                    return View(ticket);
                }
                catch (KeyNotFoundException)
                {
                    return NotFound();
                }
            }

            [HttpGet]
            [Authorize]
            public IActionResult Create()
            {
                ViewBag.Categories = Enum.GetValues(typeof(TicketCategory))
                    .Cast<TicketCategory>()
                    .Select(c => new SelectListItem
                    {
                        Value = c.ToString(),
                        Text = c.ToString()
                    }).ToList();

                ViewBag.Priorities = Enum.GetValues(typeof(TicketPriority))
                    .Cast<TicketPriority>()
                    .Select(p => new SelectListItem
                    {
                        Value = p.ToString(),
                        Text = p.ToString()
                    }).ToList();

                ViewBag.Statuses = Enum.GetValues(typeof(TicketStatus))
                    .Cast<TicketStatus>()
                    .Select(s => new SelectListItem
                    {
                        Value = s.ToString(),
                        Text = s.ToString()
                    }).ToList();

                ViewBag.Attachments = new List<Attachment>();

                return View(new Ticket
                {
                    Status = TicketStatus.New,
                    Source = "Web"
                });
            }

            [HttpPost]
            [Authorize]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> Create(Ticket ticket)
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                ticket.CreatedByUserId = userId;
                ticket.CreatedDate = DateTime.Now;
                ticket.UpdatedDate = DateTime.Now;
                ticket.Status = TicketStatus.New;

                // Supprimer les erreurs sur les relations non postées
                ModelState.Remove(nameof(ticket.CreatedByUserId));
                ModelState.Remove(nameof(ticket.CreatedByUser));
                ModelState.Remove(nameof(ticket.AssignedToUser));
                ModelState.Remove(nameof(ticket.AssignedToTeam));
                ModelState.Remove(nameof(ticket.TicketComments));
                ModelState.Remove(nameof(ticket.TicketHistories));
                ModelState.Remove(nameof(ticket.TicketAttachments));

                if (ModelState.IsValid)
                {
                    try
                    {
                        // 1. Enregistrer le ticket
                        var createdTicket = await _ticketService.CreateTicketAsync(ticket);
                        _logger.LogInformation($"Ticket #{createdTicket.TicketID} créé avec succès.");

                        // 2. Commentaire initial
                        var commentText = Request.Form["InitialComment"];
                        if (!string.IsNullOrWhiteSpace(commentText))
                        {
                            var comment = new TicketComment
                            {
                                TicketID = createdTicket.TicketID,
                                UserId = userId,
                                CommentText = commentText,
                                IsInternal = false,
                                CreatedDate = DateTime.Now
                            };
                            await _commentService.AddCommentAsync(comment);
                            _logger.LogInformation($"Commentaire initial ajouté au ticket #{createdTicket.TicketID}");
                        }

                        // 3. Ajouter pièce jointe
                        var file = Request.Form.Files.FirstOrDefault();
                        if (file != null && file.Length > 0)
                        {
                            var attachment = new Attachment
                            {
                                TicketID = createdTicket.TicketID,
                                FileName = Path.GetFileName(file.FileName),
                                UploaderUserId = userId,
                                UploadedDate = DateTime.Now
                            };

                            using var memoryStream = new MemoryStream();
                            await file.CopyToAsync(memoryStream);
                            memoryStream.Position = 0;
                            await _attachmentService.AddAttachmentAsync(attachment, memoryStream);
                            _logger.LogInformation($"Pièce jointe ajoutée au ticket #{createdTicket.TicketID}");
                        }

                        // 4. UTILISER LA NOUVELLE MÉTHODE UNIFIÉE D'ASSIGNATION AUTOMATIQUE
                        try
                        {
                            // Réinitialiser le suivi des entités pour éviter les problèmes de cache
                            _context.ChangeTracker.Clear();

                            // Utiliser la méthode unifiée du service d'assignation
                            await _assignmentRuleService.AssignTicketAutomaticallyAsync(createdTicket.TicketID);
                            _logger.LogInformation($"Assignation automatique du ticket #{createdTicket.TicketID} terminée avec succès");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Erreur lors de l'assignation automatique du ticket #{createdTicket.TicketID}");
                        }

                        TempData["SuccessMessage"] = "✅ Ticket créé avec succès!";
                        return RedirectToAction(nameof(MyTickets));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"ERREUR GLOBALE lors de la création du ticket: {ex.Message}");
                        _logger.LogError($"StackTrace: {ex.StackTrace}");
                        ModelState.AddModelError("", $"❌ Erreur lors de la création : {ex.InnerException?.Message ?? ex.Message}");
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ ModelState invalid:");
                    foreach (var key in ModelState.Keys)
                    {
                        var state = ModelState[key];
                        foreach (var err in state.Errors)
                        {
                            _logger.LogWarning($"Champ: {key} ➤ Erreur: {err.ErrorMessage}");
                        }
                    }
                }

                // Recharger dropdowns si erreur
                ViewBag.Categories = Enum.GetValues(typeof(TicketCategory))
                    .Cast<TicketCategory>()
                    .Select(c => new SelectListItem { Value = c.ToString(), Text = c.ToString() })
                    .ToList();

                ViewBag.Priorities = Enum.GetValues(typeof(TicketPriority))
                    .Cast<TicketPriority>()
                    .Select(p => new SelectListItem { Value = p.ToString(), Text = p.ToString() })
                    .ToList();

                ViewBag.Statuses = Enum.GetValues(typeof(TicketStatus))
                    .Cast<TicketStatus>()
                    .Select(s => new SelectListItem { Value = s.ToString(), Text = s.ToString() })
                    .ToList();

                return View(ticket);
            }

            // GET: Ticket/Edit/5
            [HttpGet]
            [Authorize]
            public async Task<IActionResult> Edit(int id)
            {
                try
                {
                    var ticket = await _ticketService.GetTicketByIdAsync(id);

                    // Vérifier si l'utilisateur est autorisé à modifier ce ticket
                    string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (!User.IsInRole("Admin") && !User.IsInRole("SupportAgent") && ticket.CreatedByUserId != userId)
                    {
                        return Forbid();
                    }

                    // Préparer les listes déroulantes
                    ViewBag.Categories = Enum.GetValues(typeof(TicketCategory))
                        .Cast<TicketCategory>()
                        .Select(c => new SelectListItem
                        {
                            Value = c.ToString(),
                            Text = c.ToString(),
                            Selected = c.Equals(ticket.Category)
                        }).ToList();

                    ViewBag.Priorities = Enum.GetValues(typeof(TicketPriority))
                        .Cast<TicketPriority>()
                        .Select(p => new SelectListItem
                        {
                            Value = p.ToString(),
                            Text = p.ToString(),
                            Selected = p.Equals(ticket.Priority)
                        }).ToList();

                    ViewBag.Statuses = Enum.GetValues(typeof(TicketStatus))
                        .Cast<TicketStatus>()
                        .Select(s => new SelectListItem
                        {
                            Value = s.ToString(),
                            Text = s.ToString(),
                            Selected = s.Equals(ticket.Status)
                        }).ToList();

                    // Si c'est un admin ou un agent, préparer la liste des agents et des équipes
                    if (User.IsInRole("Admin") || User.IsInRole("SupportAgent"))
                    {
                        ViewBag.Agents = await _userManager.GetUsersInRoleAsync("SupportAgent");
                        ViewBag.Teams = await _supportTeamService.GetAllAsync();
                    }

                    return View(ticket);
                }
                catch (KeyNotFoundException)
                {
                    return NotFound();
                }
            }

            // POST: Ticket/Edit/5
            [HttpPost]
            [Authorize]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> Edit(int id, Ticket ticket)
            {
                if (id != ticket.TicketID)
                    return BadRequest();

                try
                {
                    var existingTicket = await _ticketService.GetTicketByIdAsync(id);
                    string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                    if (!User.IsInRole("Admin") && !User.IsInRole("SupportAgent") && existingTicket.CreatedByUserId != userId)
                    {
                        return Forbid();
                    }

                    // Conserver certaines valeurs existantes qui ne doivent pas être modifiées
                    ticket.CreatedByUserId = existingTicket.CreatedByUserId;
                    ticket.CreatedDate = existingTicket.CreatedDate;

                    ModelState.Remove("CreatedByUser");
                    ModelState.Remove("AssignedToUser");
                    ModelState.Remove("AssignedToTeam");
                    ModelState.Remove("TicketComments");
                    ModelState.Remove("TicketHistories");
                    ModelState.Remove("TicketAttachments");

                    if (ModelState.IsValid)
                    {
                        try
                        {
                            // Only admins and support agents can modify the escalation status
                            if (!User.IsInRole("Admin") && !User.IsInRole("SupportAgent"))
                            {
                                ticket.IsEscalated = existingTicket.IsEscalated;
                            }

                            // Passer l'ID de l'utilisateur qui fait la modification
                            ticket.UpdatedByUserId = userId;
                            await _ticketService.UpdateTicketAsync(ticket);

                            // If ticket category or priority has changed, re-apply assignment rules
                            if (existingTicket.Category != ticket.Category || existingTicket.Priority != ticket.Priority)
                            {
                                await _assignmentRuleService.AssignTicketAutomaticallyAsync(ticket.TicketID);
                                TempData["SuccessMessage"] = "Ticket updated successfully and re-assigned based on rules!";
                            }
                            else
                            {
                                TempData["SuccessMessage"] = "Ticket updated successfully!";
                            }

                            return RedirectToAction(nameof(Details), new { id = ticket.TicketID });
                        }
                        catch (KeyNotFoundException)
                        {
                            return NotFound();
                        }
                        catch (Exception ex)
                        {
                            ModelState.AddModelError("", $"Unable to update ticket: {ex.Message}");
                        }
                    }

                    // Recréer les listes déroulantes en cas d'erreur
                    ViewBag.Categories = Enum.GetValues(typeof(TicketCategory))
                        .Cast<TicketCategory>()
                        .Select(c => new SelectListItem
                        {
                            Value = c.ToString(),
                            Text = c.ToString(),
                            Selected = c.Equals(ticket.Category)
                        }).ToList();

                    ViewBag.Priorities = Enum.GetValues(typeof(TicketPriority))
                        .Cast<TicketPriority>()
                        .Select(p => new SelectListItem
                        {
                            Value = p.ToString(),
                            Text = p.ToString(),
                            Selected = p.Equals(ticket.Priority)
                        }).ToList();

                    ViewBag.Statuses = Enum.GetValues(typeof(TicketStatus))
                        .Cast<TicketStatus>()
                        .Select(s => new SelectListItem
                        {
                            Value = s.ToString(),
                            Text = s.ToString(),
                            Selected = s.Equals(ticket.Status)
                        }).ToList();

                    if (User.IsInRole("Admin") || User.IsInRole("SupportAgent"))
                    {
                        ViewBag.Agents = await _userManager.GetUsersInRoleAsync("SupportAgent");
                        ViewBag.Teams = await _supportTeamService.GetAllAsync();
                    }

                    return View(ticket);
                }
                catch (KeyNotFoundException)
                {
                    return NotFound();
                }
            }

            [HttpGet]
            [Authorize]
            public async Task<IActionResult> Delete(int id)
            {
                try
                {
                    var ticket = await _ticketService.GetTicketByIdAsync(id);

                    // Check if user has permission to delete
                    string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (!User.IsInRole("Admin") && ticket.CreatedByUserId != userId)
                    {
                        return Forbid();
                    }

                    return View(ticket);
                }
                catch (KeyNotFoundException)
                {
                    return NotFound();
                }
            }

            [HttpPost, ActionName("Delete")]
            [Authorize]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> DeleteConfirmed(int id)
            {
                try
                {
                    // Get the ticket first to check permissions
                    var ticket = await _ticketService.GetTicketByIdAsync(id);
                    if (ticket == null)
                    {
                        return NotFound();
                    }

                    // Check if user has permission to delete
                    string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (!User.IsInRole("Admin") && ticket.CreatedByUserId != userId)
                    {
                        return Forbid();
                    }

                    // Delete related entities manually
                    await DeleteTicketRelatedEntitiesAsync(id);

                    // Now delete the ticket
                    await _ticketService.DeleteTicketAsync(id);

                    TempData["SuccessMessage"] = "Ticket deleted successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (KeyNotFoundException)
                {
                    return NotFound();
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Error deleting ticket: {ex.Message}";
                    return RedirectToAction(nameof(Index));
                }
            }

            // Helper method to delete ticket-related entities
            // Méthode utilitaire pour supprimer les entités liées au ticket
            private async Task DeleteTicketRelatedEntitiesAsync(int ticketId)
            {
                _logger.LogInformation($"Suppression des entités liées au ticket {ticketId}");

                var strategy = _context.Database.CreateExecutionStrategy();

                await strategy.ExecuteAsync(async () =>
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        // Supprimer les pièces jointes
                        var attachments = await _context.Attachments
                            .Where(a => a.TicketID == ticketId)
                            .ToListAsync();

                        if (attachments.Any())
                        {
                            _logger.LogInformation($"Suppression de {attachments.Count} pièces jointes");
                            _context.Attachments.RemoveRange(attachments);
                        }

                        // Supprimer les commentaires
                        var comments = await _context.TicketComments
                            .Where(c => c.TicketID == ticketId)
                            .ToListAsync();

                        if (comments.Any())
                        {
                            _logger.LogInformation($"Suppression de {comments.Count} commentaires");
                            _context.TicketComments.RemoveRange(comments);
                        }

                        // Supprimer l'historique
                        var history = await _context.TicketHistories
                            .Where(h => h.TicketID == ticketId)
                            .ToListAsync();

                        if (history.Any())
                        {
                            _logger.LogInformation($"Suppression de {history.Count} entrées d'historique");
                            _context.TicketHistories.RemoveRange(history);
                        }

                        // Sauvegarder les changements
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();
                        _logger.LogInformation($"Suppression des entités liées au ticket {ticketId} effectuée avec succès");
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(ex, $"Erreur lors de la suppression des entités liées au ticket {ticketId}: {ex.Message}");
                        throw;
                    }
                });
            }

            // GET: Ticket/ChangeStatus/5?status=Resolved
            [HttpGet]
            [Authorize(Roles = "Admin,SupportAgent")]
            public async Task<IActionResult> ChangeStatus(int id, TicketStatus status)
            {
                try
                {
                    string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    await _ticketService.ChangeTicketStatusAsync(id, status, userId);

                    // Check if ticket should be escalated after status change
                    await _escalationRuleService.CheckAndEscalateTicketAsync(id);

                    TempData["SuccessMessage"] = $"Ticket status changed to {status}";
                    return RedirectToAction(nameof(Details), new { id });
                }
                catch (KeyNotFoundException)
                {
                    return NotFound();
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = ex.Message;
                    return RedirectToAction(nameof(Details), new { id });
                }
            }

            // GET: Ticket/Assign/5
            [HttpGet]
            [Authorize(Roles = "Admin,SupportAgent")]
            public async Task<IActionResult> Assign(int id)
            {
                try
                {
                    var ticket = await _ticketService.GetTicketByIdAsync(id);

                    // Récupérer la liste des agents pour le dropdown
                    ViewBag.Agents = await _userManager.GetUsersInRoleAsync("SupportAgent");
                    ViewBag.Teams = await _supportTeamService.GetAllAsync();

                    return View(ticket);
                }
                catch (KeyNotFoundException)
                {
                    return NotFound();
                }
            }

            // POST: Ticket/Assign/5
            [HttpPost]
            [Authorize(Roles = "Admin,SupportAgent")]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> Assign(int id, string assignedToUserId)
            {
                try
                {
                    string updatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    await _ticketService.AssignTicketAsync(id, assignedToUserId, updatedByUserId);
                    TempData["SuccessMessage"] = "Ticket assigned successfully!";
                    return RedirectToAction(nameof(Details), new { id });
                }
                catch (KeyNotFoundException)
                {
                    return NotFound();
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", ex.Message);

                    // Récupérer à nouveau la liste des agents pour le dropdown
                    ViewBag.Agents = await _userManager.GetUsersInRoleAsync("SupportAgent");
                    ViewBag.Teams = await _supportTeamService.GetAllAsync();

                    var ticket = await _ticketService.GetTicketByIdAsync(id);
                    return View(ticket);
                }
            }

            // POST: Ticket/AssignTeam/5
            [HttpPost]
            [Authorize(Roles = "Admin,SupportAgent")]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> AssignTeam(int id, int teamId)
            {
                try
                {
                    string updatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    await _ticketService.AssignTicketToTeamAsync(id, teamId, updatedByUserId);
                    TempData["SuccessMessage"] = "Ticket assigned to team successfully!";
                    return RedirectToAction(nameof(Details), new { id });
                }
                catch (KeyNotFoundException)
                {
                    return NotFound();
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = ex.Message;
                    return RedirectToAction(nameof(Details), new { id });
                }
            }

            // POST: Ticket/ApplyAssignmentRule/5
            [HttpPost]
            [Authorize(Roles = "Admin,SupportAgent")]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> ApplyAssignmentRule(int id)
            {
                try
                {
                    // Utiliser la méthode améliorée d'assignation automatique
                    await _assignmentRuleService.AssignTicketAutomaticallyAsync(id);
                    TempData["SuccessMessage"] = "Assignment rules applied successfully!";
                    return RedirectToAction(nameof(Details), new { id });
                }
                catch (KeyNotFoundException)
                {
                    return NotFound();
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Error applying assignment rules: {ex.Message}";
                    return RedirectToAction(nameof(Details), new { id });
                }
            }

            // POST: Ticket/EscalateTicket/5
            [HttpPost]
            [Authorize(Roles = "Admin,SupportAgent")]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> EscalateTicket(int id)
            {
                try
                {
                    var ticket = await _ticketService.GetTicketByIdAsync(id);
                    var rules = await _escalationRuleService.GetAllRulesAsync();
                    var rule = rules.FirstOrDefault(r =>
                        (r.Priority == null || r.Priority == ticket.Priority) &&
                        (r.Status == null || r.Status == ticket.Status));

                    if (rule != null)
                    {
                        await _escalationRuleService.EscalateTicketAsync(id, rule);
                        TempData["SuccessMessage"] = "Ticket escalated successfully!";
                    }
                    else
                    {
                        TempData["WarningMessage"] = "No matching escalation rule found for this ticket.";
                    }

                    return RedirectToAction(nameof(Details), new { id });
                }
                catch (KeyNotFoundException)
                {
                    return NotFound();
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Error escalating ticket: {ex.Message}";
                    return RedirectToAction(nameof(Details), new { id });
                }
            }

            // GET: Ticket/Search
            [HttpGet]
            public IActionResult Search()
            {
                return View();
            }

            // POST: Ticket/Search
            [HttpPost]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> Search(string searchTerm)
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    ModelState.AddModelError("", "Please enter a search term");
                    return View();
                }

                var tickets = await _ticketService.SearchTicketsAsync(searchTerm);
                return View("SearchResults", tickets);
            }

            // GET: Ticket/FilterByStatus/Open
            [HttpGet]
            public async Task<IActionResult> FilterByStatus(TicketStatus status)
            {
                var tickets = await _ticketService.GetTicketsByStatusAsync(status);
                ViewData["CurrentFilter"] = $"Status: {status}";
                return View("Index", tickets);
            }

            // POST: Ticket/ForceAssignTicket
            [HttpPost]
            [Authorize(Roles = "Admin")]
            public async Task<IActionResult> ForceAssignTicket(int ticketId)
            {
                try
                {
                    await _assignmentRuleService.AssignTicketToLeastBusyAgentAsync(ticketId);
                    TempData["SuccessMessage"] = "Ticket has been assigned to the least busy support agent.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error forcing ticket assignment: {ex.Message}");
                    TempData["ErrorMessage"] = $"Error assigning ticket: {ex.Message}";
                }

                return RedirectToAction("Details", new { id = ticketId });
            }

            // GET: Ticket/UnassignedTickets
            [HttpGet]
            [Authorize(Roles = "Admin")]
            public async Task<IActionResult> UnassignedTickets()
            {
                var allTickets = await _ticketService.GetAllTicketsAsync();
                var unassignedTickets = allTickets.Where(t => string.IsNullOrEmpty(t.AssignedToUserId)).ToList();

                return View(unassignedTickets);
            }

            // POST: Ticket/AssignAllUnassignedTickets
            [HttpPost]
            [Authorize(Roles = "Admin")]
            public async Task<IActionResult> AssignAllUnassignedTickets()
            {
                try
                {
                    var allTickets = await _ticketService.GetAllTicketsAsync();
                    var unassignedTickets = allTickets.Where(t => string.IsNullOrEmpty(t.AssignedToUserId)).ToList();

                    int count = 0;
                    int failed = 0;

                    foreach (var ticket in unassignedTickets)
                    {
                        try
                        {
                            // Utiliser la méthode automatique d'assignation
                            await _assignmentRuleService.AssignTicketAutomaticallyAsync(ticket.TicketID);

                            // Vérifier si l'assignation a réussi
                            var updatedTicket = await _ticketRepository.GetByIdAsync(ticket.TicketID);
                            if (!string.IsNullOrEmpty(updatedTicket.AssignedToUserId))
                            {
                                count++;
                            }
                            else
                            {
                                failed++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Erreur lors de l'assignation du ticket {ticket.TicketID}: {ex.Message}");
                            failed++;
                        }
                    }

                    if (count > 0)
                    {
                        TempData["SuccessMessage"] = $"{count} tickets ont été assignés avec succès.";
                    }

                    if (failed > 0)
                    {
                        TempData["ErrorMessage"] = $"{failed} tickets n'ont pas pu être assignés automatiquement. Vérifiez les logs pour plus de détails.";
                    }

                    if (count == 0 && failed == 0)
                    {
                        TempData["InfoMessage"] = "Aucun ticket à assigner.";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Erreur générale lors de l'assignation des tickets: {ex.Message}");
                    TempData["ErrorMessage"] = $"Erreur lors de l'opération: {ex.Message}";
                }

                return RedirectToAction(nameof(UnassignedTickets));
            }

            // GET: Ticket/FilterByPriority/High
            [HttpGet]
            public async Task<IActionResult> FilterByPriority(TicketPriority priority)
            {
                var tickets = await _ticketService.GetTicketsByPriorityAsync(priority);
                ViewData["CurrentFilter"] = $"Priority: {priority}";
                return View("Index", tickets);
            }

            // GET: Ticket/FilterByCategory/HardwareIssue
            [HttpGet]
            public async Task<IActionResult> FilterByCategory(TicketCategory category)
            {
                var tickets = await _ticketService.GetTicketsByCategoryAsync(category);
                ViewData["CurrentFilter"] = $"Category: {category}";
                return View("Index", tickets);
            }

            // GET: Ticket/Dashboard
            [HttpGet]
            [Authorize(Roles = "Admin,SupportAgent")]
            public async Task<IActionResult> Dashboard()
            {
                // Récupérer les statistiques des tickets
                var statusDistribution = await _ticketService.GetTicketStatusDistributionAsync();
                var priorityDistribution = await _ticketService.GetTicketPriorityDistributionAsync();

                // Récupérer les tickets récents
                var recentTickets = (await _ticketService.GetAllTicketsAsync())
                    .OrderByDescending(t => t.CreatedDate)
                    .Take(10);

                // Récupérer les tickets nécessitant une escalade
                var ticketsNeedingEscalation = await _escalationRuleService.GetTicketsNeedingEscalationAsync();

                ViewBag.StatusDistribution = statusDistribution;
                ViewBag.PriorityDistribution = priorityDistribution;
                ViewBag.RecentTickets = recentTickets;
                ViewBag.TicketsNeedingEscalation = ticketsNeedingEscalation;

                return View();
            }
        }
    }
}