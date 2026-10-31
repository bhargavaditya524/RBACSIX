Role Based Access Control used in coaching management


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

## Role-aware coaching assistant

The authenticated assistant uses live, role-filtered coaching records alongside Admin-managed global policy documents. It does not train a model on student data or allow model-generated SQL. Parents are limited to linked children, Staff to assigned batches, and Students to their own records. Uploaded documents are visible to all authenticated portal roles, so only upload center-wide material approved for that audience.

Configure an OpenAI-compatible chat and embedding provider. Store the API key in user secrets during development:

```powershell
dotnet user-secrets set "CoachingAssistant:ApiKey" "<provider-api-key>" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "CoachingAssistant:ChatApiUrl" "https://openrouter.ai/api/v1/chat/completions" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "CoachingAssistant:EmbeddingApiUrl" "https://openrouter.ai/api/v1/embeddings" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "CoachingAssistant:ChatModel" "<chat-model-id>" --project .\RBACSIX\RBACSIX.csproj
dotnet user-secrets set "CoachingAssistant:EmbeddingModel" "<embedding-model-id>" --project .\RBACSIX\RBACSIX.csproj
```

In production, use the corresponding environment variables, such as `CoachingAssistant__ApiKey`. The defaults use OpenRouter-compatible endpoints; choose models supported by the configured provider. The API key is required for both chat answers and document embeddings.

Knowledge documents (PDF, DOCX, or TXT, up to 25 MB) and extracted chunks are stored under `RBACSIX\App_Data\knowledge_base` by default. Configure `CoachingAssistant:DocumentRootPath` to use a protected persistent directory in production, and include that directory in backup and retention policies. Document text and the user's question are sent to the configured AI provider; do not upload private student records or sensitive documents. Deleting a document removes its indexed chunks and stored original.

After deploying the new schema, apply the generated assistant migration using the normal database deployment process. Do not expose the standalone chatbot's unauthenticated API in place of the role-protected coaching portal endpoints.

## 📸 Screenshots



### Login Page

<img width="1905" height="775" alt="image" src="https://github.com/user-attachments/assets/5715967f-c41f-4a47-91c1-b02d9ea0eb90" />

### Login Page (Responsive)
<img width="326" height="586" alt="login page" src="https://github.com/user-attachments/assets/5a2ce376-e01a-4717-ba0f-d3ba43490f8c" />

### Admin Dashboard
<img width="1862" height="776" alt="admin dashboard" src="https://github.com/user-attachments/assets/0033acfe-9e3b-4fe0-83c0-17e504350015" />

### Staff Dashboard
<img width="1507" height="780" alt="staff dashboard" src="https://github.com/user-attachments/assets/38e32dd6-2d15-4187-98e5-0cf35359a313" />
<img width="992" height="777" alt="staff dashboard 2" src="https://github.com/user-attachments/assets/2e61b2cc-e4f8-4e92-80c5-78ba5af1fa0d" />

### Student Dashboard
<img width="1460" height="777" alt="student dashbord" src="https://github.com/user-attachments/assets/33e1b5b8-63c3-4b51-a13b-08bf3145bf04" />

### Parent/Guardian Dashboard
<img width="1847" height="787" alt="guardian dashboard" src="https://github.com/user-attachments/assets/8283a42b-d290-4954-887f-491c65811383" />
