-- Tables principales

-- Table des utilisateurs
CREATE TABLE Users (
    UserID INT IDENTITY(1,1) PRIMARY KEY,
    Username NVARCHAR(50) NOT NULL UNIQUE,
    Email NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(128) NOT NULL,
    FirstName NVARCHAR(50),
    LastName NVARCHAR(50),
    PhoneNumber NVARCHAR(20),
    CreatedDate DATETIME NOT NULL DEFAULT GETDATE(),
    LastLoginDate DATETIME,
    IsActive BIT NOT NULL DEFAULT 1
);

-- Table des rôles
CREATE TABLE Roles (
    RoleID INT IDENTITY(1,1) PRIMARY KEY,
    RoleName NVARCHAR(50) NOT NULL UNIQUE,
    Description NVARCHAR(255)
);

-- Table de liaison utilisateurs-rôles (many-to-many)
CREATE TABLE UserRoles (
    UserID INT NOT NULL,
    RoleID INT NOT NULL,
    AssignedDate DATETIME NOT NULL DEFAULT GETDATE(),
    PRIMARY KEY (UserID, RoleID),
    FOREIGN KEY (UserID) REFERENCES Users(UserID),
    FOREIGN KEY (RoleID) REFERENCES Roles(RoleID)
);

-- Table des catégories de tickets
CREATE TABLE TicketCategories (
    CategoryID INT IDENTITY(1,1) PRIMARY KEY,
    CategoryName NVARCHAR(100) NOT NULL UNIQUE,
    Description NVARCHAR(255),
    ParentCategoryID INT NULL,
    FOREIGN KEY (ParentCategoryID) REFERENCES TicketCategories(CategoryID)
);

-- Table des priorités de tickets
CREATE TABLE TicketPriorities (
    PriorityID INT IDENTITY(1,1) PRIMARY KEY,
    PriorityName NVARCHAR(50) NOT NULL UNIQUE,
    Description NVARCHAR(255),
    SLAResponseHours INT NOT NULL,
    SLAResolutionHours INT NOT NULL
);

-- Table des statuts de tickets
CREATE TABLE TicketStatuses (
    StatusID INT IDENTITY(1,1) PRIMARY KEY,
    StatusName NVARCHAR(50) NOT NULL UNIQUE,
    Description NVARCHAR(255),
    IsClosedStatus BIT NOT NULL DEFAULT 0
);

-- Table des équipes de support
CREATE TABLE SupportTeams (
    TeamID INT IDENTITY(1,1) PRIMARY KEY,
    TeamName NVARCHAR(100) NOT NULL UNIQUE,
    Description NVARCHAR(255),
    ManagerID INT NULL,
    FOREIGN KEY (ManagerID) REFERENCES Users(UserID)
);

-- Table de liaison utilisateurs-équipes (many-to-many)
CREATE TABLE TeamMembers (
    TeamID INT NOT NULL,
    UserID INT NOT NULL,
    JoinDate DATETIME NOT NULL DEFAULT GETDATE(),
    PRIMARY KEY (TeamID, UserID),
    FOREIGN KEY (TeamID) REFERENCES SupportTeams(TeamID),
    FOREIGN KEY (UserID) REFERENCES Users(UserID)
);

-- Table principale des tickets
CREATE TABLE Tickets (
    TicketID INT IDENTITY(1000,1) PRIMARY KEY,
    Title NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NOT NULL,
    CategoryID INT NOT NULL,
    PriorityID INT NOT NULL,
    StatusID INT NOT NULL,
    CreatedByUserID INT NOT NULL,
    AssignedToUserID INT NULL,
    AssignedToTeamID INT NULL,
    CreatedDate DATETIME NOT NULL DEFAULT GETDATE(),
    UpdatedDate DATETIME NOT NULL DEFAULT GETDATE(),
    DueDate DATETIME NULL,
    ResolutionDate DATETIME NULL,
    ClosedDate DATETIME NULL,
    Source NVARCHAR(50) NOT NULL, -- Web, Email, API, etc.
    IsEscalated BIT NOT NULL DEFAULT 0,
    FOREIGN KEY (CategoryID) REFERENCES TicketCategories(CategoryID),
    FOREIGN KEY (PriorityID) REFERENCES TicketPriorities(PriorityID),
    FOREIGN KEY (StatusID) REFERENCES TicketStatuses(StatusID),
    FOREIGN KEY (CreatedByUserID) REFERENCES Users(UserID),
    FOREIGN KEY (AssignedToUserID) REFERENCES Users(UserID),
    FOREIGN KEY (AssignedToTeamID) REFERENCES SupportTeams(TeamID)
);

-- Table des commentaires sur les tickets
CREATE TABLE TicketComments (
    CommentID INT IDENTITY(1,1) PRIMARY KEY,
    TicketID INT NOT NULL,
    UserID INT NOT NULL,
    CommentText NVARCHAR(MAX) NOT NULL,
    CommentDate DATETIME NOT NULL DEFAULT GETDATE(),
    IsInternal BIT NOT NULL DEFAULT 0, -- True pour les notes internes, False pour les commentaires publics
    FOREIGN KEY (TicketID) REFERENCES Tickets(TicketID),
    FOREIGN KEY (UserID) REFERENCES Users(UserID)
);

-- Table des pièces jointes
CREATE TABLE Attachments (
    AttachmentID INT IDENTITY(1,1) PRIMARY KEY,
    TicketID INT NOT NULL,
    CommentID INT NULL, -- NULL si rattaché directement au ticket
    FileName NVARCHAR(255) NOT NULL,
    FileSize BIGINT NOT NULL,
    ContentType NVARCHAR(100) NOT NULL,
    FilePath NVARCHAR(500) NOT NULL,
    UploadedByUserID INT NOT NULL,
    UploadDate DATETIME NOT NULL DEFAULT GETDATE(),
    FOREIGN KEY (TicketID) REFERENCES Tickets(TicketID),
    FOREIGN KEY (CommentID) REFERENCES TicketComments(CommentID),
    FOREIGN KEY (UploadedByUserID) REFERENCES Users(UserID)
);

-- Table d'historique des tickets (audit trail)
CREATE TABLE TicketHistory (
    HistoryID INT IDENTITY(1,1) PRIMARY KEY,
    TicketID INT NOT NULL,
    FieldName NVARCHAR(100) NOT NULL, -- Nom du champ modifié
    OldValue NVARCHAR(MAX) NULL,
    NewValue NVARCHAR(MAX) NULL,
    ChangedByUserID INT NOT NULL,
    ChangedDate DATETIME NOT NULL DEFAULT GETDATE(),
    FOREIGN KEY (TicketID) REFERENCES Tickets(TicketID),
    FOREIGN KEY (ChangedByUserID) REFERENCES Users(UserID)
);

-- Table des règles d'attribution automatique
CREATE TABLE AssignmentRules (
    RuleID INT IDENTITY(1,1) PRIMARY KEY,
    RuleName NVARCHAR(100) NOT NULL UNIQUE,
    Description NVARCHAR(255),§
    PriorityID INT NULL, -- NULL pour toutes les priorités
    AssignToTeamID INT NULL, -- Soit équipe, soit utilisateur
    AssignToUserID INT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    RuleOrder INT NOT NULL, -- Ordre d'évaluation des règles
    FOREIGN KEY (CategoryID) REFERENCES TicketCategories(CategoryID),
    FOREIGN KEY (PriorityID) REFERENCES TicketPriorities(PriorityID),
    FOREIGN KEY (AssignToTeamID) REFERENCES SupportTeams(TeamID),
    FOREIGN KEY (AssignToUserID) REFERENCES Users(UserID)
);

-- Table des règles d'escalade
CREATE TABLE EscalationRules (
    RuleID INT IDENTITY(1,1) PRIMARY KEY,
    RuleName NVARCHAR(100) NOT NULL UNIQUE,
    Description NVARCHAR(255),
    PriorityID INT NULL, -- NULL pour toutes les priorités
    StatusID INT NULL, -- NULL pour tous les statuts
    EscalateAfterHours INT NOT NULL, -- Heures avant escalade
    EscalateToUserID INT NULL,
    EscalateToTeamID INT NULL,
    NotifyUserIDs NVARCHAR(MAX) NULL, -- Liste d'IDs d'utilisateurs à notifier (format JSON)
    IsActive BIT NOT NULL DEFAULT 1,
    FOREIGN KEY (PriorityID) REFERENCES TicketPriorities(PriorityID),
    FOREIGN KEY (StatusID) REFERENCES TicketStatuses(StatusID),
    FOREIGN KEY (EscalateToUserID) REFERENCES Users(UserID),
    FOREIGN KEY (EscalateToTeamID) REFERENCES SupportTeams(TeamID)
);

-- Table des notifications
CREATE TABLE Notifications (
    NotificationID INT IDENTITY(1,1) PRIMARY KEY,
    UserID INT NOT NULL,
    TicketID INT NOT NULL,
    NotificationType NVARCHAR(50) NOT NULL, -- 'Assignment', 'Comment', 'Status Change', etc.
    Message NVARCHAR(MAX) NOT NULL,
    IsRead BIT NOT NULL DEFAULT 0,
    CreatedDate DATETIME NOT NULL DEFAULT GETDATE(),
    FOREIGN KEY (UserID) REFERENCES Users(UserID),
    FOREIGN KEY (TicketID) REFERENCES Tickets(TicketID)
);

-- Table pour la base de connaissances
CREATE TABLE KnowledgeBaseArticles (
    ArticleID INT IDENTITY(1,1) PRIMARY KEY,
    Title NVARCHAR(255) NOT NULL,
    Content NVARCHAR(MAX) NOT NULL,
    CategoryID INT NULL,
    AuthorID INT NOT NULL,
    CreatedDate DATETIME NOT NULL DEFAULT GETDATE(),
    UpdatedDate DATETIME NOT NULL DEFAULT GETDATE(),
    IsPublished BIT NOT NULL DEFAULT 0,
    ViewCount INT NOT NULL DEFAULT 0,
    FOREIGN KEY (CategoryID) REFERENCES TicketCategories(CategoryID),
    FOREIGN KEY (AuthorID) REFERENCES Users(UserID)
);

-- Table des préférences utilisateur
CREATE TABLE UserPreferences (
    UserID INT PRIMARY KEY,
    EmailNotifications BIT NOT NULL DEFAULT 1,
    InAppNotifications BIT NOT NULL DEFAULT 1,
    DarkModeEnabled BIT NOT NULL DEFAULT 0,
    ItemsPerPage INT NOT NULL DEFAULT 20,
    DefaultDashboard NVARCHAR(50) NOT NULL DEFAULT 'default',
    FOREIGN KEY (UserID) REFERENCES Users(UserID)
);

-- Index pour optimiser les performances
CREATE INDEX IX_Tickets_StatusID ON Tickets(StatusID);
CREATE INDEX IX_Tickets_PriorityID ON Tickets(PriorityID);
CREATE INDEX IX_Tickets_CategoryID ON Tickets(CategoryID);
CREATE INDEX IX_Tickets_AssignedToUserID ON Tickets(AssignedToUserID);
CREATE INDEX IX_Tickets_AssignedToTeamID ON Tickets(AssignedToTeamID);
CREATE INDEX IX_Tickets_CreatedDate ON Tickets(CreatedDate);
CREATE INDEX IX_Tickets_DueDate ON Tickets(DueDate);
CREATE INDEX IX_TicketComments_TicketID ON TicketComments(TicketID);
CREATE INDEX IX_TicketHistory_TicketID ON TicketHistory(TicketID);
CREATE INDEX IX_Notifications_UserID ON Notifications(UserID);
CREATE INDEX IX_Notifications_IsRead ON Notifications(IsRead);

-- Données initiales pour certaines tables
-- Rôles
INSERT INTO Roles (RoleName, Description) VALUES 
('Admin', 'Administrateurs système avec accès complet'),
('Manager', 'Gestionnaires d''équipe avec accès aux rapports et à la gestion des tickets'),
('Agent', 'Agents de support qui traitent les tickets'),
('User', 'Utilisateurs standard qui peuvent créer et consulter leurs propres tickets');

-- Statuts
INSERT INTO TicketStatuses (StatusName, Description, IsClosedStatus) VALUES 
('New', 'Ticket nouvellement créé', 0),
('In Progress', 'Ticket en cours de traitement par un agent', 0),
('On Hold', 'Ticket en attente d''information ou d''action', 0),
('Resolved', 'Ticket résolu mais en attente de confirmation', 0),
('Closed', 'Ticket fermé et complété', 1),
('Cancelled', 'Ticket annulé', 1);

-- Priorités
INSERT INTO TicketPriorities (PriorityName, Description, SLAResponseHours, SLAResolutionHours) VALUES 
('Low', 'Problème mineur sans impact significatif', 24, 72),
('Medium', 'Problème avec impact limité', 12, 48),
('High', 'Problème avec impact important', 4, 24),
('Critical', 'Problème urgent avec impact majeur', 1, 8);


