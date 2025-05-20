using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Ticketing_System.Models;
using Ticketing_System.Service_Layer.Interfaces;
using Ticketing_System.Service_Layer.Service;

namespace Ticketing_System.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly IUserService _userService;
        private readonly RoleManager<Role> _roleManager;
        private readonly UserManager<User> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IAssignmentRuleService _assignmentRuleService;
        private readonly ILogger<AdminController> _logger;
        private readonly CompleteUserDeletionService _deletionService;

        public AdminController(
            IUserService userService,
            RoleManager<Role> roleManager,
            UserManager<User> userManager,
            ApplicationDbContext context,
            IAssignmentRuleService assignmentRuleService,
            ILogger<AdminController> logger,
            CompleteUserDeletionService deletionService)
        {
            _userService = userService;
            _roleManager = roleManager;
            _userManager = userManager;
            _context = context;
            _assignmentRuleService = assignmentRuleService;
            _logger = logger;
            _deletionService = deletionService;
        }

        [HttpGet]
        public async Task<IActionResult> TestAssignment()
        {
            try
            {
                // Get assignment rules
                var rules = await _assignmentRuleService.GetAllRulesAsync();
                var ruleInfo = rules.Select(r => new
                {
                    RuleID = r.RuleID,
                    RuleName = r.RuleName,
                    AssignToUserID = r.AssignToUserID,
                    AssignToTeamID = r.AssignToTeamID,
                    IsActive = r.IsActive
                }).ToList();

                // Get team members
                var teamMembers = await _context.TeamMembers
                    .Include(tm => tm.User)
                    .Include(tm => tm.Team)
                    .ToListAsync();

                var teamMemberInfo = teamMembers.Select(tm => new
                {
                    TeamMemberID = tm.TeamMemberID,
                    TeamID = tm.TeamID,
                    TeamName = tm.Team?.TeamName,
                    UserID = tm.UserId,
                    UserName = tm.User?.UserName
                }).ToList();

                // Get support agents
                var supportAgents = new List<object>();
                var users = await _userManager.GetUsersInRoleAsync("SupportAgent");
                foreach (var user in users)
                {
                    var ticketCount = await _context.Tickets
                        .CountAsync(t => t.AssignedToUserId == user.Id);

                    supportAgents.Add(new
                    {
                        UserID = user.Id,
                        UserName = user.UserName,
                        TicketCount = ticketCount
                    });
                }

                return Json(new
                {
                    Rules = ruleInfo,
                    TeamMembers = teamMemberInfo,
                    SupportAgents = supportAgents
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    Error = ex.Message,
                    StackTrace = ex.StackTrace
                });
            }
        }

        // GET: Admin/Users
        [HttpGet]
        public async Task<IActionResult> Users(string searchTerm = "")
        {
            IEnumerable<User> users;

            if (!string.IsNullOrEmpty(searchTerm))
            {
                users = await _userService.SearchUsersAsync(searchTerm);
                ViewBag.SearchTerm = searchTerm;
            }
            else
            {
                users = await _userService.GetAllUsersAsync();
            }

            // For each user, get their roles
            var userRoles = new Dictionary<string, IEnumerable<string>>();
            foreach (var user in users)
            {
                userRoles[user.Id] = await _userService.GetUserRolesAsync(user.Id);
            }

            ViewBag.UserRoles = userRoles;
            return View(users);
        }

        // GET: Admin/UserDetails/5
        [HttpGet]
        public async Task<IActionResult> UserDetails(string id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            ViewBag.UserRoles = await _userService.GetUserRolesAsync(id);
            return View(user);
        }

        // GET: Admin/EditUser/5
        [HttpGet]
        public async Task<IActionResult> EditUser(string id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var userRoles = await _userService.GetUserRolesAsync(id);
            var allRoles = _roleManager.Roles.Select(r => r.Name).ToList();

            ViewBag.Roles = new MultiSelectList(allRoles, userRoles);
            return View(user);
        }

        // POST: Admin/EditUser/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(string id, User user, string[] selectedRoles)
        {
            if (id != user.Id)
            {
                return BadRequest();
            }

            try
            {
                await _userService.UpdateUserAsync(user);

                if (selectedRoles != null)
                {
                    await _userService.UpdateUserRolesAsync(id, selectedRoles);
                }

                TempData["SuccessMessage"] = "User updated successfully!";
                return RedirectToAction(nameof(Users));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                var allRoles = _roleManager.Roles.Select(r => r.Name).ToList();
                ViewBag.Roles = new MultiSelectList(allRoles, selectedRoles);
                return View(user);
            }
        }

        // GET: Admin/DeleteUser/5
        [HttpGet]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            ViewBag.UserRoles = await _userService.GetUserRolesAsync(id);
            return View(user);
        }

        // AdminController.cs - Fixed User Deletion Method
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUserConfirmed(string id)
        {
            try
            {
                // Utiliser le service de suppression complète
                var deletionService = new CompleteUserDeletionService(
                    _context,
                    HttpContext.RequestServices.GetRequiredService<ILogger<CompleteUserDeletionService>>());

                await deletionService.DeleteUserCompletelyAsync(id);
                TempData["SuccessMessage"] = "User deleted successfully!";
                return RedirectToAction(nameof(Users));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting user: {ex.Message}";
                return RedirectToAction(nameof(Users));
            }
        }


        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            ViewBag.TotalUsers = await _userService.GetTotalUsersAsync();
            ViewBag.OpenTickets = await _userService.GetTicketsByStatusAsync("Open");
            ViewBag.ResolvedTickets = await _userService.GetTicketsByStatusAsync("Resolved");
            ViewBag.ClosedTickets = await _userService.GetTicketsByStatusAsync("Closed");

            ViewBag.HighPriorityTickets = await _userService.GetTicketsByPriorityAsync("High");
            ViewBag.MediumPriorityTickets = await _userService.GetTicketsByPriorityAsync("Medium");
            ViewBag.LowPriorityTickets = await _userService.GetTicketsByPriorityAsync("Low");

            // 👇 Ajouter ces listes pour les graphiques
            var ticketsStatus = new List<object>
            {
                new { Label = "Open", Value = ViewBag.OpenTickets },
                new { Label = "Resolved", Value = ViewBag.ResolvedTickets },
                new { Label = "Closed", Value = ViewBag.ClosedTickets }
            };

            var ticketsPriority = new List<object>
            {
                new { Label = "High", Value = ViewBag.HighPriorityTickets },
                new { Label = "Medium", Value = ViewBag.MediumPriorityTickets },
                new { Label = "Low", Value = ViewBag.LowPriorityTickets }
            };

            ViewBag.TicketsStatusData = ticketsStatus;
            ViewBag.TicketsPriorityData = ticketsPriority;

            return View();
        }

        // GET: Admin/CreateUser
        [HttpGet]
        public IActionResult CreateUser()
        {
            var allRoles = _roleManager.Roles.Select(r => r.Name).ToList();
            ViewBag.Roles = new MultiSelectList(allRoles);
            return View();
        }

        // POST: Admin/CreateUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(User user, string password, string[] selectedRoles)
        {
            ModelState.Remove("Comments");
            ModelState.Remove("Articles");
            ModelState.Remove("Attachments");
            ModelState.Remove("ManagedTeams");
            ModelState.Remove("Notifications");
            ModelState.Remove("TicketChanges");
            ModelState.Remove("CreatedTickets");
            ModelState.Remove("AssignedTickets");
            ModelState.Remove("TeamMemberships");
            if (ModelState.IsValid)
            {
                user.UserName = user.Email;
                user.CreatedDate = DateTime.Now;
                user.IsActive = true;

                var result = await _userManager.CreateAsync(user, password);
                if (result.Succeeded)
                {
                    if (selectedRoles != null && selectedRoles.Any())
                    {
                        await _userManager.AddToRolesAsync(user, selectedRoles);
                    }

                    TempData["SuccessMessage"] = "User created successfully!";
                    return RedirectToAction(nameof(Users));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }

            var allRoles = _roleManager.Roles.Select(r => r.Name).ToList();
            ViewBag.Roles = new MultiSelectList(allRoles, selectedRoles);
            return View(user);
        }
    }
}