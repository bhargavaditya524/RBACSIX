

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

## Coaching Center Workflows

The portal uses ASP.NET Core Identity accounts linked to student, staff, or parent profiles. The seeded roles are `Admin`, `Staff`, `Student`, and `Parent`.

- **Admin:** create Student, Staff, Parent, and additional Admin accounts; approve Staff registrations; link Parents to children; edit/reassign/delete courses and batches; set batch capacity; search students by name; enroll students; review course/batch requests; manage each enrollment's tuition, miscellaneous charges, and payments; and view enrollment, fee, and attendance reports.
- **Staff:** self-register subject to Admin approval; view assigned batches, manage batch study materials and assignments, grade submissions, post batch announcements, record attendance/results, schedule Zoom classes, approve or reject leave requests for assigned batches, and view assigned-batch attendance/results reports.
- **Student:** self-register and sign in immediately; view enrolled batch materials, Zoom classes, attendance, results, and fee balances; submit assignments; review grades and feedback; read batch/global announcements; request leave or additional course/batch enrollments; register a linked Parent login; and view personal attendance/results/fee charts.
- **Parent:** view read-only attendance (with approved leave excluded), results, and fees for explicitly linked children, and request leave on their behalf.
- **Admin:** manage coaching accounts and batches, publish global announcements visible to every signed-in role.
- **Shared:** sign in and role-aware navigation. Controllers enforce role checks, and student records are loaded only through the signed-in account's linked student profile.

### First run

1. Configure `ConnectionStrings:DefaultConnection` for SQL Server.
2. Apply migrations with `dotnet ef database update --project RBACSIX/RBACSIX.csproj`.
3. Run the app and open `/Account/SetupAdmin` (or use the first-time setup link on the sign-in page) to create the first administrator. That setup page returns 404 after an administrator exists.
4. Sign in as the administrator to create courses, batches, student/staff/parent accounts, and enrollments. Parent access is granted by an Admin-created account with one or more explicitly linked students.

Batch capacity is optional; a blank value means unlimited. Enrollment is rejected once the configured capacity is reached. Course default tuition applies to enrollments unless the Admin sets a student-specific override. Miscellaneous charges and payments are tracked per enrollment and visible as a remaining balance to the student and linked parent. The Admin can permanently delete a course or batch; associated Zoom meetings must be canceled successfully before database records are deleted.

Students and Parents can submit date-range leave requests for enrolled batches. Assigned Staff approve or reject the requests. Approved leave dates are excluded from attendance totals and percentages.

The coaching migration keeps existing Identity account rows and removes the old EF inheritance discriminator column while transitioning the Identity context to the custom `Users` type. Existing accounts remain available for login; legacy student/staff accounts are not automatically assigned new profiles, so migrate those profiles before using the coaching dashboards.

Apply all schema changes with `dotnet ef database update --project .\RBACSIX\RBACSIX.csproj`.

### Registration, enrollment requests, and reports

Students can register from the sign-in page and are signed in immediately. Staff can also self-register, but their Staff features remain blocked until an Admin approves the account from **Staff approvals**. Existing Admin-created Student and Staff accounts continue to work. Only the initial setup or an authenticated Admin can create Admin accounts; public registration never accepts a role selection. A Student can create a Parent login and link it to their own profile immediately; they should securely share the Parent's credentials.

Students can request another active course/batch from **Course/batch requests**. The request does not enroll them. Admins review requests from the dashboard or **Enrollment requests**; approval checks that the Student is not already enrolled, that the course is active, and that batch capacity has not been reached. A request that cannot be approved because the batch is full is rejected with a note and no enrollment is created.

**Reports** are role-scoped: Admins see enrollment, payment/balance, and attendance overviews; Staff see attendance and results only for currently assigned batches; Students see only their own attendance, results, and fee balances. Charts include a data table alternative and require no external chart service.

### Zoom class setup

Zoom class scheduling and deletion require an activated Zoom Server-to-Server OAuth app with the `meeting:write:meeting:admin` and `meeting:delete:meeting:admin` scopes enabled. Set the Account ID, Client ID, and Client Secret locally; do not commit credentials to `appsettings*.json`:

```powershell
dotnet user-secrets set "Zoom:AccountId" "YOUR_ZOOM_ACCOUNT_ID" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "Zoom:ClientId" "YOUR_ZOOM_CLIENT_ID" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "Zoom:ClientSecret" "YOUR_ZOOM_CLIENT_SECRET" --project .\RBACSIX\RBACSIX.csproj
```

The app can start without Zoom credentials, but Zoom class creation stays disabled and displays setup instructions until they are configured. Partial Zoom credentials stop startup with a configuration error. The default class time zone is `Asia/Kolkata`. Change `Coaching:TimeZoneId` in local configuration (or set `Coaching__TimeZoneId` in the environment) to a supported time-zone ID for your coaching center. Staff schedule meetings from **My batches → Create Zoom class**; only students enrolled in that batch see the class join link. The app stores the Zoom meeting ID and attendee join URL, not the host start URL or OAuth access token.

Completed classes are removed from student and staff lists as soon as their scheduled duration ends. A hosted cleanup task then cancels the Zoom meeting and deletes its database record; it checks again while the app is running and catches up after restart. If Zoom cannot cancel a meeting, the record is retained for retry. Staff can manually delete a class they created at any time; this also cancels the Zoom meeting, and the record is retained if cancellation fails.

### Study materials, assignments, and announcements

Staff open **My batches → Learning content** to add a PDF or HTTP/HTTPS link as study material, post assignments, review submissions, grade them, and publish announcements for that batch. Students access **Materials & assignments** for batches in which they are enrolled. Each student may submit one file plus optional comments, and replace it until Staff grades it. Grading stores a numeric score (up to the assignment's maximum) and written feedback. Assignments do not have due dates in this version. Admin publishes global announcements at **Global announcements**; global posts are visible to all signed-in roles. Staff batch announcements are restricted to the assigned Staff and enrolled students.

Uploads are limited to 20 MB. Study materials accept PDF; student submissions accept PDF, DOCX, XLSX, PPTX, PNG, JPEG, GIF, and WEBP. Files are stored under `RBACSIX\App_Data\uploads` by default, outside `wwwroot`, with generated storage names and database metadata. Set `Files:RootPath` to another private server directory if needed; configuration that points beneath the public web root is rejected. Back up this directory together with SQL Server. The upload directory is excluded from Git.

If Zoom returns an API error, the scheduling page shows its safe error code/message; logs contain the same redacted diagnostic. Zoom recommends checking the response body for the specific error. Never paste account credentials or access tokens into chat, source code, or committed configuration. If credentials were exposed, rotate the app credentials in the Zoom Marketplace, deactivate the old app secret, then update the local `Zoom:AccountId`, `Zoom:ClientId`, and `Zoom:ClientSecret` user-secrets values and restart the app. The app obtains its access token automatically; never copy or configure that token manually. Run the request-contract tests with `dotnet test .\RBACSIX.Tests\RBACSIX.Tests.csproj`.

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
•	Students: View course details and attendance, join live classes, and request enrollment in additional courses.
