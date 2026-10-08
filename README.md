# 🔐 Role-Based Access Control (RBAC) System

A web-based **Role-Based Access Control (RBAC)** system built using **ASP.NET Core MVC, Entity Framework Core, ASP.NET Core Identity, and Microsoft SQL Server**.

The project demonstrates how authentication and authorization can be implemented to manage users, roles, and access permissions dynamically.

## 🚀 Features

- 🔑 User Registration & Login
- 🔒 Secure Authentication using ASP.NET Core Identity
- 👥 Dynamic User Management
- 🛡️ Role-Based Authorization
- ➕ Create and Manage Roles
- 👤 Assign Roles to Users
- 🚫 Restrict pages based on user roles
- 📊 Admin Dashboard
- 🔄 CRUD operations for users and roles
- 🗄️ SQL Server database integration
- ⚡ Entity Framework Core
- 🧩 MVC architecture

## 🛠️ Technologies Used

### Backend
- ASP.NET Core MVC
- C#
- Entity Framework Core
- ASP.NET Core Identity

### Database
- Microsoft SQL Server
- SQL

### Frontend
- HTML5
- CSS3
- Bootstrap
- Razor Views

### Tools
- Visual Studio
- SQL Server Management Studio (SSMS)
- Git & GitHub

## 🏗️ Project Architecture

```text
RBAC Project
│
├── Controllers
│   ├── AccountController
│   ├── AdminController
│   ├── RoleController
│   └── UserController
│
├── Models
│   ├── ApplicationUser
│   └── ViewModels
│
├── Data
│   └── ApplicationDbContext
│
├── Views
│   ├── Account
│   ├── Admin
│   ├── Role
│   ├── User
│   └── Shared
│
├── Areas
│   └── Identity
│
├── wwwroot
│   ├── css
│   ├── js
│   └── lib
│
├── appsettings.json
└── Program.cs
```

## 🔐 Authentication & Authorization

The application uses **ASP.NET Core Identity** for authentication and role management.

### Authentication

Authentication verifies **who the user is**.

```text
User
 ↓
Login
 ↓
ASP.NET Core Identity
 ↓
Credentials Verified
 ↓
Authenticated User
```

### Authorization

Authorization determines **what an authenticated user is allowed to access**.

```text
Authenticated User
        ↓
     User Role
        ↓
 ┌───────────────┐
 │ Admin / User  │
 └───────────────┘
        ↓
 Access Based on Role
```

For example:

```csharp
[Authorize(Roles = "Admin")]
public IActionResult Dashboard()
{
    return View();
}
```

Only users with the **Admin** role can access the dashboard.

## 👥 Role Management

Administrators can manage roles dynamically instead of having roles hard-coded throughout the application.

Example roles:

- Admin
- User
- Manager

The admin can:

1. Create a new role
2. View existing roles
3. Edit roles
4. Delete roles
5. Assign roles to users

## 👤 User Management

The system provides an interface for administrators to manage users.

Admin can:

- View registered users
- Create users
- Assign roles
- Change user roles
- Manage access levels

## 🗄️ Database

The application uses **Microsoft SQL Server** with **Entity Framework Core**.

ASP.NET Core Identity manages tables such as:

```text
AspNetUsers
AspNetRoles
AspNetUserRoles
AspNetUserClaims
AspNetRoleClaims
AspNetUserLogins
AspNetUserTokens
```

The relationship between users and roles is managed through the user-role mapping.

```text
User
 │
 │
 ▼
AspNetUserRoles
 │
 │
 ▼
Role
```

## ⚙️ Setup & Installation

### 1. Clone the Repository

```bash
git clone https://github.com/your-username/your-rbac-project.git
```

### 2. Open the Project

Open the project in **Visual Studio**.

### 3. Configure SQL Server

Update the connection string in:

```text
appsettings.json
```

Example:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=RBACDB;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

Replace `YOUR_SERVER` with your SQL Server instance.

### 4. Apply Database Migrations

Open Package Manager Console and run:

```powershell
Update-Database
```

Or using the .NET CLI:

```bash
dotnet ef database update
```

### 5. Run the Application

```bash
dotnet run
```

Or press **F5** in Visual Studio.

## 🔑 Example Access Control

| Feature | Admin | User |
|---|:---:|:---:|
| Login | ✅ | ✅ |
| View Dashboard | ✅ | ❌ |
| View Users | ✅ | ❌ |
| Create User | ✅ | ❌ |
| Manage Roles | ✅ | ❌ |
| Assign Roles | ✅ | ❌ |
| Access User Pages | ✅ | ✅ |

## 🔄 Application Flow

```text
              ┌─────────────┐
              │    User     │
              └──────┬──────┘
                     │
                  Login
                     │
                     ▼
          ┌────────────────────┐
          │ ASP.NET Core       │
          │ Identity           │
          └─────────┬──────────┘
                    │
              Authentication
                    │
                    ▼
              ┌───────────┐
              │   Role    │
              └─────┬─────┘
                    │
           ┌────────┴────────┐
           ▼                 ▼
        Admin              User
           │                 │
           ▼                 ▼
    Admin Dashboard     User Features
```

## 🧠 What I Learned

Through this project, I gained practical experience with:

- ASP.NET Core MVC architecture
- ASP.NET Core Identity
- Authentication vs Authorization
- Role-based authorization
- Entity Framework Core
- SQL Server database design
- User-role relationships
- CRUD operations
- Secure access control
- Razor Views
- Middleware and request pipeline
- Building an admin dashboard

## 📸 Screenshots

Add screenshots of the application here.

### Login Page

```text
Add screenshot here
```

### Admin Dashboard

```text
Add screenshot here
```

### User Management

```text
Add screenshot here
```

### Role Management

```text
Add screenshot here
```

## 🔮 Future Improvements

- [ ] Email verification
- [ ] Forgot/reset password
- [ ] Two-factor authentication
- [ ] Permission-based authorization
- [ ] Audit logs
- [ ] Improved dashboard analytics
- [ ] REST API integration
- [ ] React frontend
- [ ] Deployment to Azure

## 👨‍💻 Author

**Aditya Bhargava**

B.Tech CSE Student | ASP.NET Core Developer

### Skills

`C#` `ASP.NET Core MVC` `Entity Framework Core` `SQL Server` `ASP.NET Core Identity` `REST API` `React.js`

---

⭐ If you find this project useful, consider giving it a star!
