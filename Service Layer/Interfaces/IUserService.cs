using Ticketing_System.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ticketing_System.Service_Layer.Interfaces
{
    public interface IUserService
    {
        Task<IEnumerable<User>> GetAllUsersAsync();
        Task<User> GetUserByIdAsync(string userId);
        Task<IEnumerable<User>> GetUsersByRoleAsync(string roleName);
        Task<bool> UpdateUserAsync(User user);
        Task<bool> DeleteUserAsync(string userId);
        Task<bool> DeactivateUserAsync(string userId);
        Task<bool> UpdateProfileAsync(User userProfile);
        Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
        Task<IEnumerable<User>> SearchUsersAsync(string searchTerm);
        Task<bool> IsEmailUniqueAsync(string email, string excludeUserId = null);
        Task<IEnumerable<string>> GetUserRolesAsync(string userId);
        Task<bool> UpdateUserRolesAsync(string userId, IEnumerable<string> roles);

        Task<int> GetTotalUsersAsync();
Task<int> GetTicketsByStatusAsync(string status);
Task<int> GetTicketsByPriorityAsync(string priority);
        
    }
}