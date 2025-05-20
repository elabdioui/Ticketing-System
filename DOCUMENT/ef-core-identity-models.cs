// Models/ApplicationDbContext.cs
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TicketingSystem.Models
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<TicketCategory> TicketCategories { get; set; }
        public DbSet<TicketPriority> TicketPriorities { get; set; }
        public DbSet<TicketStatus> TicketStatuses { get; set; }
        public DbSet<SupportTeam> SupportTeams { get; set; }
        public DbSet<TeamMember> TeamMembers { get; set; }
        public DbSet<Ticket> Tickets { get; set; }
        public DbSet<TicketComment> TicketComments { get; set; }
        public DbSet<Attachment> Attachments { get; set; }
        public DbSet<TicketHistory> TicketHistory { get; set; }
        public DbSet<AssignmentRule> AssignmentRules { get; set; }
        public DbSet<EscalationRule> EscalationRules { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<KnowledgeBaseArticle> KnowledgeBaseArticles { get; set; }
        public DbSet<UserPreference> UserPreferences { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configurer les clés composites
            modelBuilder.Entity<TeamMember>()
                .HasKey(tm => new { tm.TeamID, tm.UserID });

            // Configurer les relations
            modelBuilder.Entity<Ticket>()
                .HasOne(t => t.CreatedByUser)
                .WithMany(u => u.CreatedTickets)
                .HasForeignKey(t => t.CreatedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Ticket>()
                .HasOne(t => t.AssignedToUser)
                .WithMany(u => u.AssignedTickets)
                .HasForeignKey(t => t.AssignedToUserID)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            modelBuilder.Entity<TicketCategory>()
                .HasOne(c => c.ParentCategory)
                .WithMany(c => c.ChildCategories)
                .HasForeignKey(c => c.ParentCategoryID)
                .IsRequired(false);

            modelBuilder.Entity<Attachment>()
                .HasOne(a => a.Comment)
                .WithMany(c => c.Attachments)
                .HasForeignKey(a => a.CommentID)
                .IsRequired(false);

            // Index
            modelBuilder.Entity<Ticket>()
                .HasIndex(t => t.StatusID);
            
            modelBuilder.Entity<Ticket>()
                .HasIndex(t => t.PriorityID);
            
            modelBuilder.Entity<Ticket>()
                .HasIndex(t => t.CategoryID);
            
            modelBuilder.Entity<Ticket>()
                .HasIndex(t => t.AssignedToUserID);
            
            modelBuilder.Entity<Ticket>()
                .HasIndex(t => t.AssignedToTeamID);
            
            modelBuilder.Entity<Ticket>()
                .HasIndex(t => t.CreatedDate);
            
            modelBuilder.Entity<Ticket>()
                .HasIndex(t => t.DueDate);
            
            modelBuilder.Entity<TicketComment>()
                .HasIndex(tc => tc.TicketID);
            
            modelBuilder.Entity<TicketHistory>()
                .HasIndex(th => th.TicketID);
            
            modelBuilder.Entity<Notification>()
                .HasIndex(n => n.UserID);
            
            modelBuilder.Entity<Notification>()
                .HasIndex(n => n.IsRead);

            // Configuration des données initiales
            SeedInitialData(modelBuilder);
        }

        private void SeedInitialData(ModelBuilder modelBuilder)
        {
            // Initialiser les statuts des tickets
            modelBuilder.Entity<TicketStatus>().HasData(
                new TicketStatus { StatusID = 1, StatusName = "New", Description = "Ticket nouvellement créé", IsClosedStatus = false },
                new TicketStatus { StatusID = 2, StatusName = "In Progress", Description = "Ticket en cours de traitement par un agent", IsClosedStatus = false },
                new TicketStatus { StatusID = 3, StatusName = "On Hold", Description = "Ticket en attente d'information ou d'action", IsClosedStatus = false },
                new TicketStatus { StatusID = 4, StatusName = "Resolved", Description = "Ticket résolu mais en attente de confirmation", IsClosedStatus = false },
                new TicketStatus { StatusID = 5, StatusName = "Closed", Description = "Ticket fermé et complété", IsClosedStatus = true },
                new TicketStatus { StatusID = 6, StatusName = "Cancelled", Description = "Ticket annulé", IsClosedStatus = true }
            );

            // Initialiser les priorités des tickets
            modelBuilder.Entity<TicketPriority>().HasData(
                new TicketPriority { PriorityID = 1, PriorityName = "Low", Description = "Problème mineur sans impact significatif", SLAResponseHours = 24, SLAResolutionHours = 72 },
                new TicketPriority { PriorityID = 2, PriorityName = "Medium", Description = "Problème avec impact limité", SLAResponseHours = 12, SLAResolutionHours = 48 },
                new TicketPriority { PriorityID = 3, PriorityName = "High", Description = "Problème avec impact important", SLAResponseHours = 4, SLAResolutionHours = 24 },
                new TicketPriority { PriorityID = 4, PriorityName = "Critical", Description = "Problème urgent avec impact majeur", SLAResponseHours = 1, SLAResolutionHours = 8 }
            );
        }
    }

    // Models/ApplicationUser.cs
    using Microsoft.AspNetCore.Identity;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class ApplicationUser : IdentityUser
        {
            [MaxLength(50)]
            public string FirstName { get; set; }
            
            [MaxLength(50)]
            public string LastName { get; set; }
            
            [Required]
            public DateTime CreatedDate { get; set; } = DateTime.Now;
            
            public DateTime? LastLoginDate { get; set; }
            
            [Required]
            public bool IsActive { get; set; } = true;
            
            // Navigation properties
            public virtual ICollection<Ticket> CreatedTickets { get; set; }
            public virtual ICollection<Ticket> AssignedTickets { get; set; }
            public virtual ICollection<SupportTeam> ManagedTeams { get; set; }
            public virtual ICollection<TeamMember> TeamMemberships { get; set; }
            public virtual ICollection<TicketComment> Comments { get; set; }
            public virtual ICollection<Attachment> Attachments { get; set; }
            public virtual ICollection<TicketHistory> TicketChanges { get; set; }
            public virtual ICollection<KnowledgeBaseArticle> Articles { get; set; }
            public virtual ICollection<Notification> Notifications { get; set; }
            public virtual UserPreference Preference { get; set; }
        }
    }

    // Models/ApplicationRole.cs
    using Microsoft.AspNetCore.Identity;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class ApplicationRole : IdentityRole
        {
            [MaxLength(255)]
            public string Description { get; set; }
        }
    }

    // Models/TicketCategory.cs
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class TicketCategory
        {
            public int CategoryID { get; set; }
            
            [Required, MaxLength(100)]
            public string CategoryName { get; set; }
            
            [MaxLength(255)]
            public string Description { get; set; }
            
            public int? ParentCategoryID { get; set; }
            
            // Navigation properties
            public virtual TicketCategory ParentCategory { get; set; }
            public virtual ICollection<TicketCategory> ChildCategories { get; set; }
            public virtual ICollection<Ticket> Tickets { get; set; }
            public virtual ICollection<KnowledgeBaseArticle> KnowledgeBaseArticles { get; set; }
            public virtual ICollection<AssignmentRule> AssignmentRules { get; set; }
        }
    }

    // Models/TicketPriority.cs
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class TicketPriority
        {
            public int PriorityID { get; set; }
            
            [Required, MaxLength(50)]
            public string PriorityName { get; set; }
            
            [MaxLength(255)]
            public string Description { get; set; }
            
            [Required]
            public int SLAResponseHours { get; set; }
            
            [Required]
            public int SLAResolutionHours { get; set; }
            
            // Navigation properties
            public virtual ICollection<Ticket> Tickets { get; set; }
            public virtual ICollection<AssignmentRule> AssignmentRules { get; set; }
            public virtual ICollection<EscalationRule> EscalationRules { get; set; }
        }
    }

    // Models/TicketStatus.cs
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class TicketStatus
        {
            public int StatusID { get; set; }
            
            [Required, MaxLength(50)]
            public string StatusName { get; set; }
            
            [MaxLength(255)]
            public string Description { get; set; }
            
            [Required]
            public bool IsClosedStatus { get; set; } = false;
            
            // Navigation properties
            public virtual ICollection<Ticket> Tickets { get; set; }
            public virtual ICollection<EscalationRule> EscalationRules { get; set; }
        }
    }

    // Models/SupportTeam.cs
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class SupportTeam
        {
            public int TeamID { get; set; }
            
            [Required, MaxLength(100)]
            public string TeamName { get; set; }
            
            [MaxLength(255)]
            public string Description { get; set; }
            
            public string ManagerID { get; set; }
            
            // Navigation properties
            public virtual ApplicationUser Manager { get; set; }
            public virtual ICollection<TeamMember> TeamMembers { get; set; }
            public virtual ICollection<Ticket> AssignedTickets { get; set; }
            public virtual ICollection<AssignmentRule> AssignmentRules { get; set; }
            public virtual ICollection<EscalationRule> EscalationRules { get; set; }
        }
    }

    // Models/TeamMember.cs
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    namespace TicketingSystem.Models
    {
        public class TeamMember
        {
            public int TeamID { get; set; }
            public string UserID { get; set; }
            
            [Required]
            public DateTime JoinDate { get; set; } = DateTime.Now;
            
            // Navigation properties
            public virtual SupportTeam Team { get; set; }
            public virtual ApplicationUser User { get; set; }
        }
    }

    // Models/Ticket.cs
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class Ticket
        {
            public int TicketID { get; set; }
            
            [Required, MaxLength(255)]
            public string Title { get; set; }
            
            [Required]
            public string Description { get; set; }
            
            [Required]
            public int CategoryID { get; set; }
            
            [Required]
            public int PriorityID { get; set; }
            
            [Required]
            public int StatusID { get; set; }
            
            [Required]
            public string CreatedByUserID { get; set; }
            
            public string AssignedToUserID { get; set; }
            
            public int? AssignedToTeamID { get; set; }
            
            [Required]
            public DateTime CreatedDate { get; set; } = DateTime.Now;
            
            [Required]
            public DateTime UpdatedDate { get; set; } = DateTime.Now;
            
            public DateTime? DueDate { get; set; }
            
            public DateTime? ResolutionDate { get; set; }
            
            public DateTime? ClosedDate { get; set; }
            
            [Required, MaxLength(50)]
            public string Source { get; set; }
            
            [Required]
            public bool IsEscalated { get; set; } = false;
            
            // Navigation properties
            public virtual TicketCategory Category { get; set; }
            public virtual TicketPriority Priority { get; set; }
            public virtual TicketStatus Status { get; set; }
            public virtual ApplicationUser CreatedByUser { get; set; }
            public virtual ApplicationUser AssignedToUser { get; set; }
            public virtual SupportTeam AssignedToTeam { get; set; }
            public virtual ICollection<TicketComment> Comments { get; set; }
            public virtual ICollection<Attachment> Attachments { get; set; }
            public virtual ICollection<TicketHistory> History { get; set; }
            public virtual ICollection<Notification> Notifications { get; set; }
        }
    }

    // Models/TicketComment.cs
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class TicketComment
        {
            public int CommentID { get; set; }
            
            [Required]
            public int TicketID { get; set; }
            
            [Required]
            public string UserID { get; set; }
            
            [Required]
            public string CommentText { get; set; }
            
            [Required]
            public DateTime CommentDate { get; set; } = DateTime.Now;
            
            [Required]
            public bool IsInternal { get; set; } = false;
            
            // Navigation properties
            public virtual Ticket Ticket { get; set; }
            public virtual ApplicationUser User { get; set; }
            public virtual ICollection<Attachment> Attachments { get; set; }
        }
    }

    // Models/Attachment.cs
    using System;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class Attachment
        {
            public int AttachmentID { get; set; }
            
            [Required]
            public int TicketID { get; set; }
            
            public int? CommentID { get; set; }
            
            [Required, MaxLength(255)]
            public string FileName { get; set; }
            
            [Required]
            public long FileSize { get; set; }
            
            [Required, MaxLength(100)]
            public string ContentType { get; set; }
            
            [Required, MaxLength(500)]
            public string FilePath { get; set; }
            
            [Required]
            public string UploadedByUserID { get; set; }
            
            [Required]
            public DateTime UploadDate { get; set; } = DateTime.Now;
            
            // Navigation properties
            public virtual Ticket Ticket { get; set; }
            public virtual TicketComment Comment { get; set; }
            public virtual ApplicationUser UploadedByUser { get; set; }
        }
    }

    // Models/TicketHistory.cs
    using System;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class TicketHistory
        {
            public int HistoryID { get; set; }
            
            [Required]
            public int TicketID { get; set; }
            
            [Required, MaxLength(100)]
            public string FieldName { get; set; }
            
            public string OldValue { get; set; }
            
            public string NewValue { get; set; }
            
            [Required]
            public string ChangedByUserID { get; set; }
            
            [Required]
            public DateTime ChangedDate { get; set; } = DateTime.Now;
            
            // Navigation properties
            public virtual Ticket Ticket { get; set; }
            public virtual ApplicationUser ChangedByUser { get; set; }
        }
    }

    // Models/AssignmentRule.cs
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class AssignmentRule
        {
            public int RuleID { get; set; }
            
            [Required, MaxLength(100)]
            public string RuleName { get; set; }
            
            [MaxLength(255)]
            public string Description { get; set; }
            
            public int? CategoryID { get; set; }
            
            public int? PriorityID { get; set; }
            
            public int? AssignToTeamID { get; set; }
            
            public string AssignToUserID { get; set; }
            
            [Required]
            public bool IsActive { get; set; } = true;
            
            [Required]
            public int RuleOrder { get; set; }
            
            // Navigation properties
            public virtual TicketCategory Category { get; set; }
            public virtual TicketPriority Priority { get; set; }
            public virtual SupportTeam AssignToTeam { get; set; }
            public virtual ApplicationUser AssignToUser { get; set; }
        }
    }

    // Models/EscalationRule.cs
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class EscalationRule
        {
            public int RuleID { get; set; }
            
            [Required, MaxLength(100)]
            public string RuleName { get; set; }
            
            [MaxLength(255)]
            public string Description { get; set; }
            
            public int? PriorityID { get; set; }
            
            public int? StatusID { get; set; }
            
            [Required]
            public int EscalateAfterHours { get; set; }
            
            public string EscalateToUserID { get; set; }
            
            public int? EscalateToTeamID { get; set; }
            
            public string NotifyUserIDs { get; set; }
            
            [Required]
            public bool IsActive { get; set; } = true;
            
            // Navigation properties
            public virtual TicketPriority Priority { get; set; }
            public virtual TicketStatus Status { get; set; }
            public virtual ApplicationUser EscalateToUser { get; set; }
            public virtual SupportTeam EscalateToTeam { get; set; }
        }
    }

    // Models/Notification.cs
    using System;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class Notification
        {
            public int NotificationID { get; set; }
            
            [Required]
            public string UserID { get; set; }
            
            [Required]
            public int TicketID { get; set; }
            
            [Required, MaxLength(50)]
            public string NotificationType { get; set; }
            
            [Required]
            public string Message { get; set; }
            
            [Required]
            public bool IsRead { get; set; } = false;
            
            [Required]
            public DateTime CreatedDate { get; set; } = DateTime.Now;
            
            // Navigation properties
            public virtual ApplicationUser User { get; set; }
            public virtual Ticket Ticket { get; set; }
        }
    }

    // Models/KnowledgeBaseArticle.cs
    using System;
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class KnowledgeBaseArticle
        {
            public int ArticleID { get; set; }
            
            [Required, MaxLength(255)]
            public string Title { get; set; }
            
            [Required]
            public string Content { get; set; }
            
            public int? CategoryID { get; set; }
            
            [Required]
            public string AuthorID { get; set; }
            
            [Required]
            public DateTime CreatedDate { get; set; } = DateTime.Now;
            
            [Required]
            public DateTime UpdatedDate { get; set; } = DateTime.Now;
            
            [Required]
            public bool IsPublished { get; set; } = false;
            
            [Required]
            public int ViewCount { get; set; } = 0;
            
            // Navigation properties
            public virtual TicketCategory Category { get; set; }
            public virtual ApplicationUser Author { get; set; }
        }
    }

    // Models/UserPreference.cs
    using System.ComponentModel.DataAnnotations;

    namespace TicketingSystem.Models
    {
        public class UserPreference
        {
            [Key]
            public string UserID { get; set; }
            
            [Required]
            public bool EmailNotifications { get; set; } = true;
            
            [Required]
            public bool InAppNotifications { get; set; } = true;
            
            [Required]
            public bool DarkModeEnabled { get; set; } = false;
            
            [Required]
            public int ItemsPerPage { get; set; } = 20;
            
            [Required, MaxLength(50)]
            public string DefaultDashboard { get; set; } = "default";
            
            // Navigation property
            public virtual ApplicationUser User { get; set; }
        }
    }
}
