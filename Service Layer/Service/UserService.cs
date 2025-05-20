using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ticketing_System.Models;
using Ticketing_System.Repository.Interfaces;
using Ticketing_System.Repository_Pattern.Interfaces;
using Ticketing_System.Service_Layer.Interfaces;
using Ticketing_System.Service_Layer.Service;

namespace Ticketing_System.Service_Layer.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly ITicketRepository _ticketRepository;
        private readonly UserManager<User> _userManager;
        private readonly CompleteUserDeletionService _deletionService;



        public UserService(
            IUserRepository userRepository,
            ITicketRepository ticketRepository,
            CompleteUserDeletionService deletionService,
            UserManager<User> userManager)
        {
            _userRepository = userRepository;
            _ticketRepository = ticketRepository;
            _userManager = userManager;
            _deletionService = deletionService;
        }

        // ----------------- GESTION UTILISATEURS -----------------

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _userRepository.GetAllAsync();
        }

        public async Task<User> GetUserByIdAsync(string userId)
        {
            return await _userRepository.GetUserByIdAsync(userId);
        }

        public async Task<IEnumerable<User>> GetUsersByRoleAsync(string roleName)
        {
            return await _userRepository.GetUsersByRoleAsync(roleName);
        }

        public async Task<IEnumerable<User>> SearchUsersAsync(string searchTerm)
        {
            return await _userRepository.SearchUsersAsync(searchTerm);
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            return await _userRepository.UpdateUserInfoAsync(user);
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null) throw new KeyNotFoundException($"User with ID {userId} not found");

            await _userRepository.DeleteAsync(user);
            return true;
        }

        public async Task<bool> DeactivateUserAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            return await _userRepository.DeactivateUserAsync(userId);
        }

        public async Task<IEnumerable<string>> GetUserRolesAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) throw new KeyNotFoundException($"User with ID {userId} not found");

            return await _userManager.GetRolesAsync(user);
        }

        public async Task<bool> UpdateUserRolesAsync(string userId, IEnumerable<string> roles)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) throw new KeyNotFoundException($"User with ID {userId} not found");

            var currentRoles = await _userManager.GetRolesAsync(user);
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded) return false;

            var addResult = await _userManager.AddToRolesAsync(user, roles);
            return addResult.Succeeded;
        }

        public async Task<bool> IsEmailUniqueAsync(string email, string excludeUserId = null)
        {
            return await _userRepository.IsEmailUniqueAsync(email, excludeUserId);
        }

        public async Task<bool> UpdateProfileAsync(User userProfile)
        {
            if (userProfile == null) throw new ArgumentNullException(nameof(userProfile));

            var existingUser = await _userRepository.GetUserByIdAsync(userProfile.Id);
            if (existingUser == null)
                throw new KeyNotFoundException($"User with ID {userProfile.Id} not found");

            if (existingUser.Email != userProfile.Email)
            {
                if (!await _userRepository.IsEmailUniqueAsync(userProfile.Email, userProfile.Id))
                    throw new InvalidOperationException("Email is already in use");
            }

            return await _userRepository.UpdateUserInfoAsync(userProfile);
        }

        public async Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) throw new KeyNotFoundException($"User with ID {userId} not found");

            var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            return result.Succeeded;
        }

        // ----------------- STATISTIQUES DASHBOARD -----------------

        public async Task<int> GetTotalUsersAsync()
        {
            return await _userRepository.Query().CountAsync();
        }

        public async Task<int> GetTicketsByStatusAsync(string status)
        {
            if (!Enum.TryParse<TicketStatus>(status, true, out var parsedStatus))
                throw new ArgumentException("Invalid ticket status.");

            return await _ticketRepository.Query()
                .Where(t => t.Status == parsedStatus)
                .CountAsync();
        }

        public async Task<int> GetTicketsByPriorityAsync(string priority)
        {
            if (!Enum.TryParse<TicketPriority>(priority, true, out var parsedPriority))
                throw new ArgumentException("Invalid ticket priority.");

            return await _ticketRepository.Query()
                .Where(t => t.Priority == parsedPriority)
                .CountAsync();
        }
    }
}
