using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ticketing_System.Models;
using Ticketing_System.Repository.Interfaces;
using Ticketing_System.Service_Layer.Interfaces;

namespace Ticketing_System.Service_Layer.Service
{
    public class EscalationRuleService : IEscalationRuleService
    {
        private readonly IEscalationRuleRepository _ruleRepository;
        private readonly ITicketRepository _ticketRepository;
        private readonly ITicketHistoryService _historyService;
        private readonly INotificationService _notificationService;
        private readonly IUserService _userService;
        private string _systemUserId;


        public EscalationRuleService(
            IEscalationRuleRepository ruleRepository,
            ITicketRepository ticketRepository,
            ITicketHistoryService historyService,
            IUserService userService,
            INotificationService notificationService)
        {
            _ruleRepository = ruleRepository;
            _ticketRepository = ticketRepository;
            _historyService = historyService;
            _notificationService = notificationService;
            _userService = userService;
        }

        public async Task<IEnumerable<EscalationRule>> GetAllRulesAsync()
        {
            return await _ruleRepository.GetAllAsync();
        }

        public async Task<EscalationRule> GetRuleByIdAsync(int ruleId)
        {
            var rule = await _ruleRepository.GetByIdAsync(ruleId);
            if (rule == null)
            {
                throw new KeyNotFoundException($"Escalation rule with ID {ruleId} not found");
            }
            return rule;
        }

        public async Task<EscalationRule> CreateRuleAsync(EscalationRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));

            // Vérifier qu'il y a au moins un destinataire d'escalation
            if (string.IsNullOrEmpty(rule.EscalateToUserID) && !rule.EscalateToTeamID.HasValue)
            {
                throw new ArgumentException("Rule must have either a user or team for escalation");
            }

            return await _ruleRepository.AddAsync(rule);
        }

        public async Task UpdateRuleAsync(EscalationRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));

            var existingRule = await _ruleRepository.GetByIdAsync(rule.RuleID);
            if (existingRule == null)
            {
                throw new KeyNotFoundException($"Escalation rule with ID {rule.RuleID} not found");
            }

            await _ruleRepository.UpdateAsync(rule);
        }

        public async Task DeleteRuleAsync(int ruleId)
        {
            var rule = await _ruleRepository.GetByIdAsync(ruleId);
            if (rule == null)
            {
                throw new KeyNotFoundException($"Escalation rule with ID {ruleId} not found");
            }

            await _ruleRepository.DeleteAsync(rule);
        }

        public async Task<bool> CheckAndEscalateTicketAsync(int ticketId)
        {
            var ticket = await _ticketRepository.GetByIdAsync(ticketId);
            if (ticket == null || ticket.IsEscalated)
            {
                return false;
            }

            var matchingRule = await _ruleRepository.GetMatchingRuleAsync(ticket.Priority, ticket.Status);
            if (matchingRule == null)
            {
                return false;
            }

            // Calculer le temps écoulé depuis la création ou la dernière mise à jour
            var timeElapsed = DateTime.Now - ticket.UpdatedDate;
            if (timeElapsed.TotalHours >= matchingRule.EscalateAfterHours)
            {
                await EscalateTicketAsync(ticketId, matchingRule);
                return true;
            }

            return false;
        }

        public async Task<IEnumerable<Ticket>> GetTicketsNeedingEscalationAsync()
        {
            var activeRules = await _ruleRepository.GetActiveRulesAsync();
            var ticketsNeedingEscalation = new List<Ticket>();

            foreach (var rule in activeRules)
            {
                var tickets = await _ticketRepository.GetAllAsync();
                var eligibleTickets = tickets.Where(t => 
                    !t.IsEscalated &&
                    (rule.Priority == null || t.Priority == rule.Priority) &&
                    (rule.Status == null || t.Status == rule.Status) &&
                    (DateTime.Now - t.UpdatedDate).TotalHours >= rule.EscalateAfterHours);

                ticketsNeedingEscalation.AddRange(eligibleTickets);
            }

            return ticketsNeedingEscalation.Distinct().ToList();
        }

        public async Task EscalateTicketAsync(int ticketId, EscalationRule rule)
        {
            var ticket = await _ticketRepository.GetByIdAsync(ticketId);
            if (ticket == null)
            {
                throw new KeyNotFoundException($"Ticket with ID {ticketId} not found");
            }

            // Sauvegarder l'état avant escalation
            string oldUserId = ticket.AssignedToUserId;
            int? oldTeamId = ticket.AssignedToTeamID;

            // Appliquer l'escalation
            ticket.AssignedToUserId = rule.EscalateToUserID;
            ticket.AssignedToTeamID = rule.EscalateToTeamID;
            ticket.IsEscalated = true;
            ticket.UpdatedDate = DateTime.Now;

            await _ticketRepository.UpdateAsync(ticket);

            // Enregistrer l'historique
            await _historyService.AddHistoryEntryAsync(new TicketHistory
            {
                TicketID = ticketId,
                ChangedByUserId = "SYSTEM",
                FieldName = "Escalation",
                OldValue = "Not Escalated",
                NewValue = $"Escalated: {rule.RuleName}",
                ChangedDate = DateTime.Now
            });

            // Envoyer les notifications
            if (!string.IsNullOrEmpty(rule.NotifyUserIDs))
            {
                var userIds = rule.NotifyUserIDs.Split(',');
                foreach (var userId in userIds)
                {
                    if (!string.IsNullOrWhiteSpace(userId))
                    {
                        await _notificationService.CreateNotificationAsync(
                            userId.Trim(),
                            "Ticket Escalade",
                            $"Le ticket #{ticketId} - {ticket.Title} a été escaladé selon la règle: {rule.RuleName}");
                    }
                }
            }

            // Notification à l'utilisateur destinataire
            if (!string.IsNullOrEmpty(rule.EscalateToUserID))
            {
                await _notificationService.CreateNotificationAsync(
                    rule.EscalateToUserID,
                    "Nouveau ticket escaladé",
                    $"Un ticket a été escaladé et vous a été assigné: #{ticketId} - {ticket.Title}");
            }
        }
    }
}
