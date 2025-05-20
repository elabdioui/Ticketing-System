using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ticketing_System.Models;

namespace Ticketing_System.Service_Layer.Service
{
    public class CompleteUserDeletionService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CompleteUserDeletionService> _logger;

        public CompleteUserDeletionService(
            ApplicationDbContext context,
            ILogger<CompleteUserDeletionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> DeleteUserCompletelyAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentNullException(nameof(userId), "L'ID utilisateur ne peut pas être vide.");

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new KeyNotFoundException($"Utilisateur avec ID {userId} non trouvé.");

            // Utilisateur système pour remplacer les ID utilisateur quand nécessaire
            string systemUserId = await GetOrCreateSystemUserIdAsync();

            // Utiliser la stratégie d'exécution du contexte plutôt qu'une transaction explicite
            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                // Commencer une transaction avec la stratégie d'exécution approuvée
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // 1. Supprimer les notifications de l'utilisateur
                    var notifications = await _context.Notifications
                        .Where(n => n.UserId == userId)
                        .ToListAsync();

                    if (notifications.Any())
                    {
                        _context.Notifications.RemoveRange(notifications);
                        _logger.LogInformation($"Suppression de {notifications.Count} notifications");
                    }

                    // 2. Gérer les commentaires de tickets
                    var comments = await _context.TicketComments
                        .Where(tc => tc.UserId == userId)
                        .ToListAsync();

                    if (comments.Any())
                    {
                        foreach (var comment in comments)
                        {
                            // Option: anonymiser au lieu de supprimer
                            comment.UserId = systemUserId;
                            comment.CommentText += " [Utilisateur supprimé]";
                        }
                        _context.TicketComments.UpdateRange(comments);
                        _logger.LogInformation($"Anonymisation de {comments.Count} commentaires");
                    }

                    // 3. Gérer les pièces jointes
                    var attachments = await _context.Attachments
                        .Where(a => a.UploaderUserId == userId)
                        .ToListAsync();

                    if (attachments.Any())
                    {
                        foreach (var attachment in attachments)
                        {
                            attachment.UploaderUserId = systemUserId;
                        }
                        _context.Attachments.UpdateRange(attachments);
                        _logger.LogInformation($"Réassignation de {attachments.Count} pièces jointes");
                    }

                    // 4. Gérer les articles de la base de connaissances
                    var articles = await _context.KnowledgeBaseArticles
                        .Where(a => a.AuthorID == userId)
                        .ToListAsync();

                    if (articles.Any())
                    {
                        foreach (var article in articles)
                        {
                            article.AuthorID = systemUserId;
                        }
                        _context.KnowledgeBaseArticles.UpdateRange(articles);
                        _logger.LogInformation($"Réassignation de {articles.Count} articles de base de connaissances");
                    }

                    // 5. Gérer l'historique des tickets
                    var histories = await _context.TicketHistories
                        .Where(h => h.ChangedByUserId == userId)
                        .ToListAsync();

                    if (histories.Any())
                    {
                        foreach (var history in histories)
                        {
                            history.ChangedByUserId = systemUserId;
                        }
                        _context.TicketHistories.UpdateRange(histories);
                        _logger.LogInformation($"Réassignation de {histories.Count} entrées d'historique de tickets");
                    }

                    // 6. Retirer l'utilisateur des équipes qu'il gère
                    var managedTeams = await _context.SupportTeams
                        .Where(t => t.ManagerId == userId)
                        .ToListAsync();

                    if (managedTeams.Any())
                    {
                        foreach (var team in managedTeams)
                        {
                            team.ManagerId = systemUserId;
                        }
                        _context.SupportTeams.UpdateRange(managedTeams);
                        _logger.LogInformation($"Réassignation de {managedTeams.Count} équipes managées");
                    }

                    // 7. Supprimer les membres d'équipe
                    var teamMemberships = await _context.TeamMembers
                        .Where(tm => tm.UserId == userId)
                        .ToListAsync();

                    if (teamMemberships.Any())
                    {
                        _context.TeamMembers.RemoveRange(teamMemberships);
                        _logger.LogInformation($"Suppression de {teamMemberships.Count} adhésions d'équipe");
                    }

                    // 8. Gérer les tickets assignés à l'utilisateur
                    var assignedTickets = await _context.Tickets
                        .Where(t => t.AssignedToUserId == userId)
                        .ToListAsync();

                    if (assignedTickets.Any())
                    {
                        foreach (var ticket in assignedTickets)
                        {
                            ticket.AssignedToUserId = null;
                        }
                        _context.Tickets.UpdateRange(assignedTickets);
                        _logger.LogInformation($"Désassignation de {assignedTickets.Count} tickets");
                    }

                    // 9. Gérer les tickets créés par l'utilisateur
                    var createdTickets = await _context.Tickets
                        .Where(t => t.CreatedByUserId == userId)
                        .ToListAsync();

                    if (createdTickets.Any())
                    {
                        foreach (var ticket in createdTickets)
                        {
                            ticket.CreatedByUserId = systemUserId;
                        }
                        _context.Tickets.UpdateRange(createdTickets);
                        _logger.LogInformation($"Réassignation de {createdTickets.Count} tickets créés");
                    }

                    // 10. Supprimer les règles d'assignation qui référencent l'utilisateur
                    var assignmentRules = await _context.AssignmentRules
                        .Where(ar => ar.AssignToUserID == userId)
                        .ToListAsync();

                    if (assignmentRules.Any())
                    {
                        foreach (var rule in assignmentRules)
                        {
                            rule.AssignToUserID = null;
                        }
                        _context.AssignmentRules.UpdateRange(assignmentRules);
                        _logger.LogInformation($"Mise à jour de {assignmentRules.Count} règles d'assignation");
                    }

                    // 11. Supprimer les règles d'escalade qui référencent l'utilisateur
                    var escalationRules = await _context.EscalationRules
                        .Where(er => er.EscalateToUserID == userId)
                        .ToListAsync();

                    if (escalationRules.Any())
                    {
                        foreach (var rule in escalationRules)
                        {
                            rule.EscalateToUserID = null;
                        }
                        _context.EscalationRules.UpdateRange(escalationRules);
                        _logger.LogInformation($"Mise à jour de {escalationRules.Count} règles d'escalade");
                    }

                    // 12. Purger les NotifyUserIDs dans les règles d'escalation
                    var escalationRulesWithNotify = await _context.EscalationRules
                        .Where(er => er.NotifyUserIDs != null && er.NotifyUserIDs.Contains(userId))
                        .ToListAsync();

                    if (escalationRulesWithNotify.Any())
                    {
                        foreach (var rule in escalationRulesWithNotify)
                        {
                            var userIds = rule.NotifyUserIDs.Split(',').ToList();
                            userIds.Remove(userId);
                            rule.NotifyUserIDs = string.Join(",", userIds);
                        }
                        _context.EscalationRules.UpdateRange(escalationRulesWithNotify);
                        _logger.LogInformation($"Mise à jour des notifications dans {escalationRulesWithNotify.Count} règles d'escalation");
                    }

                    // 13. Supprimer les UserRoles associés à l'utilisateur
                    var userRoles = await _context.UserRoles
                        .Where(ur => ur.UserId == userId)
                        .ToListAsync();

                    if (userRoles.Any())
                    {
                        _context.UserRoles.RemoveRange(userRoles);
                        _logger.LogInformation($"Suppression de {userRoles.Count} rôles utilisateur");
                    }

                    // 14. Enfin, supprimer l'utilisateur
                    _context.Users.Remove(user);
                    _logger.LogInformation($"Suppression de l'utilisateur {user.FirstName} {user.LastName} (ID: {userId})");

                    // Sauvegarder toutes les modifications
                    await _context.SaveChangesAsync();

                    // Valider la transaction
                    await transaction.CommitAsync();

                    _logger.LogInformation($"Suppression complète de l'utilisateur {userId} effectuée avec succès");
                    return true;
                }
                catch (Exception ex)
                {
                    // Annuler la transaction en cas d'erreur
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, $"Erreur lors de la suppression complète de l'utilisateur {userId}: {ex.Message}");
                    throw new Exception($"Erreur lors de la suppression de l'utilisateur: {ex.Message}", ex);
                }
            });
        }

        // Méthode utilitaire pour obtenir ou créer un utilisateur système
        private async Task<string> GetOrCreateSystemUserIdAsync()
        {
            var systemUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == "system@quantumdesk.com");

            if (systemUser != null)
                return systemUser.Id;

            // Si l'utilisateur système n'est pas trouvé, chercher un admin
            var adminUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == "admin@admin.com");

            if (adminUser != null)
                return adminUser.Id;

            // En dernier recours, prendre le premier utilisateur disponible
            var anyUser = await _context.Users.FirstOrDefaultAsync();
            if (anyUser != null)
                return anyUser.Id;

            throw new InvalidOperationException("Aucun utilisateur système ou admin trouvé. Impossible de procéder à la suppression.");
        }
    }
}