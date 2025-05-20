use DB_PFA

select * from dbo.AspNetUsers

select * from dbo.AspNetRoles

select * from dbo.Tickets

select * from dbo.TicketComments

select * from dbo.Attachments

select * from dbo.TicketHistories




SELECT 
    u.UserName,
    r.Name AS RoleName
FROM AspNetUsers u
JOIN AspNetUserRoles ur ON u.Id = ur.UserId
JOIN AspNetRoles r ON ur.RoleId = r.Id
WHERE u.FirstName = 'Haitham';
