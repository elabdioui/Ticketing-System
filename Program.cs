using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Ticketing_System.Models;
using Ticketing_System.Repository;
using Ticketing_System.Repository.Interfaces;
using Ticketing_System.Repository_Pattern.Interfaces;
using Ticketing_System.Service_Layer;
using Ticketing_System.Service_Layer.Interfaces;
using Ticketing_System.Service_Layer.Service;
using Ticketing_System.Service_Layer.Services;
using Ticketing_System.Services;
using Microsoft.Extensions.Logging;
using Polly;

namespace Ticketing_System
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Forcer HTTP uniquement pour Docker
            builder.WebHost.UseUrls("https://+:7041");

            // Ajout de la prise en charge des variables d'environnement (critique pour Docker)
            builder.Configuration.AddEnvironmentVariables();

            // Configuration du logging pour le conteneur
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();

            // 1. Configuration de la chaîne de connexion SQL Server avec priorité aux variables d'environnement Docker
            var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"] ??
                                  builder.Configuration.GetConnectionString("DefaultConnection");

            Console.WriteLine($"Connection string being used: {connectionString}");

            // 2. Configuration des services
            ConfigureServices(builder.Services, connectionString, builder.Configuration);

            var app = builder.Build();

            // 3. Configuration du pipeline HTTP
            ConfigureMiddleware(app);

            // 4. Initialisation de la base de données avec stratégie de retry
            await InitializeDatabaseWithRetryAsync(app);

            app.Run();
        }

        private static void ConfigureServices(IServiceCollection services, string connectionString, IConfiguration configuration)
        {
            // 2.1 Configuration des repositories (Repository Pattern)
            services.AddScoped<ITicketRepository, TicketRepository>();
            services.AddScoped<ITicketCommentRepository, TicketCommentRepository>();
            services.AddScoped<ITicketHistoryRepository, TicketHistoryRepository>();
            services.AddScoped<IAttachmentRepository, AttachmentRepository>();
            services.AddScoped<ISupportTeamRepository, SupportTeamRepository>();
            services.AddScoped<INotificationRepository, NotificationRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ITeamMemberRepository, TeamMemberRepository>();
            services.AddScoped<IEscalationRuleRepository, EscalationRuleRepository>();
            services.AddScoped<IAssignmentRuleRepository, AssignmentRuleRepository>();

            // 2.2 Configuration des services (Service Layer)
            // D'abord les services qui ne dépendent pas d'autres services
            services.AddScoped<ITicketHistoryService, TicketHistoryService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IUserService, UserService>();

            // Ensuite les services qui dépendent des services précédents
            services.AddScoped<ITeamMemberService, TeamMemberService>();
            services.AddScoped<ITicketCommentService, TicketCommentService>();
            services.AddScoped<IAttachmentService, AttachmentService>();
            services.AddScoped<ISupportTeamService, SupportTeamService>();
            services.AddScoped<IEscalationRuleService, EscalationRuleService>();
            services.AddScoped<IAssignmentRuleService, AssignmentRuleService>();
            services.AddScoped<CompleteUserDeletionService>();

            // Enfin, le TicketService qui dépend de plusieurs autres services
            services.AddScoped<ITicketService, TicketService>();

            // Configuration simple des healthchecks pour Docker (sans DbContextCheck)
            services.AddHealthChecks();

            // Configuration CORS plus sécurisée
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            // Configuration pour ignorer les erreurs dans les services d'arrière-plan
            services.Configure<HostOptions>(options =>
            {
                options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
            });

            // 2.3 Services d'arrière-plan
            services.AddHostedService<EscalationBackgroundService>();

            // 2.4 Configuration de la base de données avec retry pour Docker
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 10,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                });
            });

            // 2.5 Configuration de l'identité
            services.AddDefaultIdentity<User>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

            // 2.6 Configuration des Razor Pages et MVC
            services.AddRazorPages(options =>
            {
                options.Conventions.AddPageRoute("/Identity/Account/Register", "/Register");
            });

            services.AddControllers()
                .AddJsonOptions(x =>
                {
                    x.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                    x.JsonSerializerOptions.WriteIndented = true;
                });

            services.AddControllersWithViews();

            // 2.7 Configuration de Swagger
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "QuantumDesk API",
                    Version = "v1",
                    Description = "API pour le système de ticketing QuantumDesk"
                });
            });

            // 2.8 Configuration générale
            services.AddSingleton<IConfiguration>(configuration);
        }

        private static void ConfigureMiddleware(WebApplication app)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Configuring middleware pipeline...");

            // 3.1 Gestion des erreurs et sécurité
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
                logger.LogInformation("Running in Production mode");
            }
            else
            {
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "QuantumDesk API v1"));
                logger.LogInformation("Running in Development mode with Swagger enabled");
            }

            // 3.2 Middleware HTTP
            app.UseStaticFiles();

            // 3.3 Middleware de routage
            app.UseCors("AllowAll");
            app.UseRouting();

            // Endpoint de health check pour Docker (simplifié)
            app.MapHealthChecks("/health");

            // 3.4 Middleware d'authentification et d'autorisation
            app.UseAuthentication();
            app.UseAuthorization();

            // 3.5 Mapping des routes
            app.MapControllers();
            app.MapRazorPages();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            logger.LogInformation("Middleware configuration completed successfully");
        }

        private static async Task InitializeDatabaseWithRetryAsync(WebApplication app)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Starting database initialization with retry policy");

            // Définition d'une politique de retry pour l'initialisation de la base de données
            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    10, // Nombre de tentatives
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Backoff exponentiel
                    (exception, timeSpan, retryCount, context) =>
                    {
                        logger.LogWarning($"Attempt {retryCount} to initialize database failed. Retrying in {timeSpan.TotalSeconds} seconds. Error: {exception.Message}");
                    }
                );

            await retryPolicy.ExecuteAsync(async () =>
            {
                using var scope = app.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Migrer la base de données
                logger.LogInformation("Applying database migrations...");
                await dbContext.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied successfully");

                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

                // 4.1 Création des rôles
                var roles = new[] { "Admin", "SupportAgent", "User" };
                foreach (var role in roles)
                {
                    if (!await roleManager.RoleExistsAsync(role))
                    {
                        logger.LogInformation($"Creating role: {role}");
                        await roleManager.CreateAsync(new Role { Name = role });
                    }
                }

                // 4.2 Création des utilisateurs par défaut
                logger.LogInformation("Creating default users...");
                await CreateDefaultUserAsync(userManager, logger, "admin@admin.com", "Admin@123", "Admin", "User", "Admin");
                await CreateDefaultUserAsync(userManager, logger, "system@quantumdesk.com", "SystemPassword123!", "System", "Automated", "Admin");
                await CreateDefaultUserAsync(userManager, logger, "agent@quantumdesk.com", "Agent123!", "Support", "Agent", "SupportAgent");
                logger.LogInformation("Default users created successfully");

                // 4.3 Création des données par défaut
                logger.LogInformation("Creating default support team and assignment rules...");
                await CreateDefaultSupportTeamAsync(dbContext, userManager, "admin@admin.com", "agent@quantumdesk.com");
                await CreateDefaultAssignmentRuleAsync(dbContext);
                logger.LogInformation("Default data created successfully");
            });
        }

        private static async Task CreateDefaultUserAsync(UserManager<User> userManager, ILogger logger, string email, string password, string firstName, string lastName, string role)
        {
            if (await userManager.FindByEmailAsync(email) == null)
            {
                logger.LogInformation($"Creating user: {email}");
                var user = new User
                {
                    UserName = email,
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    EmailConfirmed = true,
                    IsActive = true
                };

                var result = await userManager.CreateAsync(user, password);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, role);
                    logger.LogInformation($"User {email} created successfully with role {role}");
                }
                else
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    logger.LogWarning($"Failed to create user {email}: {errors}");
                }
            }
            else
            {
                logger.LogInformation($"User {email} already exists");
            }
        }

        private static async Task CreateDefaultSupportTeamAsync(ApplicationDbContext dbContext, UserManager<User> userManager, string adminEmail, string agentEmail)
        {
            if (!await dbContext.SupportTeams.AnyAsync())
            {
                var adminId = (await userManager.FindByEmailAsync(adminEmail))?.Id;
                if (!string.IsNullOrEmpty(adminId))
                {
                    var defaultTeam = new SupportTeam
                    {
                        TeamName = "Default Support Team",
                        Description = "Default team handling all types of tickets",
                        ManagerId = adminId
                    };

                    dbContext.SupportTeams.Add(defaultTeam);
                    await dbContext.SaveChangesAsync();

                    var agentId = (await userManager.FindByEmailAsync(agentEmail))?.Id;
                    if (!string.IsNullOrEmpty(agentId))
                    {
                        var teamMember = new TeamMember
                        {
                            TeamID = defaultTeam.TeamID,
                            UserId = agentId,
                            JoinDate = DateTime.Now
                        };

                        dbContext.TeamMembers.Add(teamMember);
                        await dbContext.SaveChangesAsync();
                    }
                }
            }
        }

        private static async Task CreateDefaultAssignmentRuleAsync(ApplicationDbContext dbContext)
        {
            if (!await dbContext.AssignmentRules.AnyAsync())
            {
                var defaultTeam = await dbContext.SupportTeams.FirstOrDefaultAsync();
                if (defaultTeam != null)
                {
                    var defaultRule = new AssignmentRule
                    {
                        RuleName = "Default Assignment Rule",
                        Description = "Assigns all tickets to the default support team",
                        Category = null, // All categories
                        Priority = null, // All priorities
                        AssignToTeamID = defaultTeam.TeamID,
                        AssignToUserID = null, // Assign to team, not specific user
                        IsActive = true,
                        RuleOrder = 1
                    };

                    dbContext.AssignmentRules.Add(defaultRule);
                    await dbContext.SaveChangesAsync();
                }
            }
        }
    }
}