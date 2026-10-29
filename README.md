

## Password recovery email configuration

The login page includes password recovery. Configure an SMTP relay and the application's public HTTPS URL before enabling it in production. Keep SMTP credentials in user secrets during development or environment variables in production; do not commit them.

For local development, set these values from the repository root, replacing the placeholders with your provider's settings:

```powershell
dotnet user-secrets set "Smtp:Host" "smtp.example.com" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "Smtp:Port" "587" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "Smtp:EnableSsl" "true" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "Smtp:UserName" "your-smtp-user" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "Smtp:Password" "your-smtp-password" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "Smtp:FromAddress" "no-reply@example.com" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "Smtp:FromName" "Coaching Portal" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "PasswordReset:PublicBaseUrl" "https://portal.example.com" --project .\RBACSIX\RBACSIX.csproj
```

In production, use the matching environment variables (for example, `Smtp__Host` and `PasswordReset__PublicBaseUrl`). A reset request always shows a generic confirmation. Development may show the generated reset link on that page; non-development environments never expose it.

## 📸 Screenshots



### Login Page

<img width="1905" height="775" alt="image" src="https://github.com/user-attachments/assets/5715967f-c41f-4a47-91c1-b02d9ea0eb90" />


### Admin Dashboard

<img width="1897" height="537" alt="image" src="https://github.com/user-attachments/assets/b5088c8c-b27f-431c-bfe3-06f1bcd08b14" />

### Staff Dashboard
<img width="1867" height="781" alt="image" src="https://github.com/user-attachments/assets/2ce7e782-f559-4acd-be03-e5a47c81909f" />
<img width="911" height="700" alt="image" src="https://github.com/user-attachments/assets/87610df7-48e2-4415-a40d-e4fb15c54102" />

### Student Dashboard
<img width="1865" height="741" alt="image" src="https://github.com/user-attachments/assets/ac042398-cabc-4d33-8951-650a5942c6ca" />

### Parent/Guardian Dashboard

<img width="1872" height="471" alt="image" src="https://github.com/user-attachments/assets/a93897d7-6963-43c0-a12b-6a04f83a6e71" />


## 🔮 Future Improvements
- [ ] Audit logs
- [ ] Deployment to Azure

## 👨‍💻 Author

**Aditya Bhargava**

B.Tech CSE Student | ASP.NET Core Developer


---

⭐ If you find this project useful, consider giving it a star!
•	Students: View course details and attendance, join live classes, and request enrollment in additional courses.
