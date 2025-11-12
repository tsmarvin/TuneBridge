# Aspire Dashboard Access Role Assignment

## Overview

The Aspire Dashboard is protected by role-based authorization using ASP.NET Core Identity. Access to the dashboard requires the `AspireDashboardAccess` role, which is **not assigned to any user by default** for security reasons.

## Prerequisites

- Access to the TuneBridge SQLite database file (default location: `/app/data/tunebridge.db` in the container)
- SQLite database client (e.g., `sqlite3` command-line tool, DB Browser for SQLite, or Azure Data Studio)
- User must be registered in the system first (via `/account/register` endpoint)

## Step-by-Step Instructions

### 1. Locate the Database

The database is stored in the persistent volume mounted at `/app/data/tunebridge.db` by default. To access it:

**Option A: From within the Docker container**
```bash
docker exec -it tunebridge sh
cd /app/data
sqlite3 tunebridge.db
```

**Option B: From the host system (if volume is bind-mounted)**
```bash
sqlite3 /path/to/tunebridge-data/tunebridge.db
```

### 2. Find the User ID

First, identify the user you want to grant access to:

```sql
SELECT Id, UserName, Email FROM AspNetUsers;
```

Note the `Id` of the user you want to grant dashboard access.

### 3. Find the Role ID

Get the ID of the `AspireDashboardAccess` role:

```sql
SELECT Id, Name FROM AspNetRoles WHERE Name = 'AspireDashboardAccess';
```

Note the `Id` of the role.

### 4. Assign the Role to the User

Insert a record into the `AspNetUserRoles` table to assign the role:

```sql
INSERT INTO AspNetUserRoles (UserId, RoleId)
VALUES ('USER_ID_HERE', 'ROLE_ID_HERE');
```

Replace `USER_ID_HERE` and `ROLE_ID_HERE` with the IDs from steps 2 and 3.

### 5. Verify the Assignment

Confirm the role assignment:

```sql
SELECT u.UserName, u.Email, r.Name as RoleName
FROM AspNetUsers u
INNER JOIN AspNetUserRoles ur ON u.Id = ur.UserId
INNER JOIN AspNetRoles r ON ur.RoleId = r.RoleId
WHERE r.Name = 'AspireDashboardAccess';
```

### 6. Exit SQLite

```sql
.quit
```

## Complete Example

Here's a complete example assuming:
- User email: `admin@example.com`
- User ID: `a1b2c3d4-e5f6-7890-abcd-ef1234567890`
- Role ID: `f9e8d7c6-b5a4-3210-fedc-ba0987654321`

```sql
-- View all users
SELECT Id, UserName, Email FROM AspNetUsers;

-- View the AspireDashboardAccess role
SELECT Id, Name FROM AspNetRoles WHERE Name = 'AspireDashboardAccess';

-- Assign the role (use actual IDs from above queries)
INSERT INTO AspNetUserRoles (UserId, RoleId)
VALUES ('a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'f9e8d7c6-b5a4-3210-fedc-ba0987654321');

-- Verify
SELECT u.UserName, u.Email, r.Name as RoleName
FROM AspNetUsers u
INNER JOIN AspNetUserRoles ur ON u.Id = ur.UserId
INNER JOIN AspNetRoles r ON ur.RoleId = r.RoleId
WHERE r.Name = 'AspireDashboardAccess';
```

## Accessing the Dashboard

Once the role is assigned:

1. **Log in** to the TuneBridge application using your credentials
2. Navigate to `https://your-domain.com/dashboard`
3. You should be granted access to the Aspire Dashboard

## Revoking Access

To remove dashboard access from a user:

```sql
DELETE FROM AspNetUserRoles
WHERE UserId = 'USER_ID_HERE'
AND RoleId = (SELECT Id FROM AspNetRoles WHERE Name = 'AspireDashboardAccess');
```

## Security Notes

- **Never assign this role to untrusted users** - the dashboard provides detailed observability data about the application
- **Audit role assignments regularly** - keep track of who has dashboard access
- **Use strong passwords** - ensure users with dashboard access have secure credentials
- **Monitor dashboard access** - check logs for unauthorized access attempts

## Troubleshooting

### User still can't access the dashboard after role assignment

1. Verify the role assignment in the database
2. Ensure the user is logged in with the correct account
3. Check TuneBridge logs for authorization failures
4. Try logging out and logging back in
5. Verify the Aspire Dashboard container is running: `docker ps | grep aspire-dashboard`

### Role doesn't exist

If the `AspireDashboardAccess` role doesn't exist, it should be created automatically on application startup. If it's missing:

1. Check TuneBridge startup logs for errors
2. Manually create the role:
   ```sql
   INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
   VALUES (lower(hex(randomblob(16))), 'AspireDashboardAccess', 'ASPIREDASHBOARDACCESS', lower(hex(randomblob(16))));
   ```

## Additional Resources

- [ASP.NET Core Identity Documentation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity)
- [SQLite Documentation](https://www.sqlite.org/docs.html)
- [Aspire Dashboard Overview](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard/overview)
