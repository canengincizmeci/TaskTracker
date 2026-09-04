# TaskTracker

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=111)](https://react.dev/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![SignalR](https://img.shields.io/badge/Realtime-SignalR-512BD4)](https://learn.microsoft.com/aspnet/core/signalr/introduction)
[![Deployment](https://img.shields.io/badge/Deployment-Ubuntu%20VPS-E95420?logo=ubuntu&logoColor=white)](https://ubuntu.com/)

**TaskTracker** is a production-deployed full-stack task management and collaboration platform built with ASP.NET Core, React, PostgreSQL, and SignalR.

It started as a task-management project and has grown into a broader engineering playground for authentication, account recovery, authorization, shared-task workflows, real-time notifications, database migrations, and Linux production operations.

## Live Application

**https://canncodehub.com**

The application is currently deployed on an Ubuntu VPS behind Nginx. The ASP.NET Core API runs as a systemd service and PostgreSQL is used as the production database.

> TaskTracker is under active development. The live application is a real deployment of the current project, not a static demo.

---

## Highlights

- Full-stack React + ASP.NET Core application
- JWT authentication with refresh-token flow
- Email verification
- Forgot-password and password-reset flow with email OTP verification
- Authenticated password change
- Refresh-token revocation after password reset or password change
- Role-based authorization
- Task CRUD operations
- Task sharing with invitation lifecycle
- Accept / reject collaboration requests
- View / edit sharing permissions
- Shared-task views
- Real-time notifications with SignalR
- Individual and bulk notification read state
- PostgreSQL with Entity Framework Core migrations
- Linux production deployment with Nginx and systemd
- Environment-based production configuration

---

## Tech Stack

### Backend

- .NET 10
- ASP.NET Core Web API
- Entity Framework Core 10
- Npgsql / PostgreSQL
- JWT Bearer Authentication
- SignalR
- Autofac
- FluentValidation
- Repository pattern
- Unit of Work pattern
- Layered / N-tier architecture

### Frontend

- React 19
- TypeScript
- Vite
- React Router
- Axios
- Microsoft SignalR client
- React Hot Toast

### Production & Infrastructure

- Ubuntu VPS
- Nginx reverse proxy
- systemd service management
- HTTPS / custom domain
- PostgreSQL
- EF Core migrations
- Environment-file based production secrets/configuration

---

## Features

### Authentication & Account Security

TaskTracker includes a complete authentication flow rather than only basic login/register endpoints.

- User registration
- User login
- Email verification
- JWT access tokens
- Refresh tokens
- Role-based authorization
- Protected frontend routes
- Forgot-password flow
- Email-based password recovery verification
- Password reset
- Authenticated password change

#### Password recovery security

The recovery flow is intentionally multi-step:

```text
Forgot password
      ↓
Email verification code
      ↓
Code verification
      ↓
Short-lived reset token
      ↓
Password reset
      ↓
Refresh-token revocation
```

Current recovery protections include:

- 6-digit verification codes
- 10-minute verification-code lifetime
- 60-second resend cooldown
- Maximum failed-attempt handling
- HMAC-SHA256 protection for recovery-code storage
- 256-bit random reset tokens
- Hashed reset-token storage
- Short-lived reset-token validity
- Invalidation of previous active recovery requests
- Revocation of existing refresh tokens after password reset
- Revocation of existing refresh tokens after authenticated password change
- Invalidation of outstanding password-reset requests after password change

The application does not automatically sign the user in after a password reset or password change.

---

### Task Management

Authenticated users can manage their own tasks through the API and React client.

- Create tasks
- List user tasks
- View task details
- Update tasks
- Delete tasks
- Protected task operations
- Priority and status support

---

### Collaboration & Task Sharing

TaskTracker supports task-level collaboration workflows.

```text
Task owner
   ↓
Invite user
   ↓
Pending invitation
   ├── Accept → shared task access
   └── Reject → invitation closed
```

Implemented capabilities include:

- Invite another user to a task
- Pending-invitation list
- Accept invitation
- Reject invitation
- View / edit permission levels
- Shared-task listing
- Shared-task detail access
- Collaboration-related notifications

---

### Real-Time Notifications

SignalR is integrated into the backend and React client for user-specific real-time notifications.

The backend sends notification events to the authenticated user's SignalR connection, while notification state remains persisted through the regular API.

Implemented notification behavior includes:

- User notification list
- Real-time notification delivery
- Mark individual notification as read
- Mark all notifications as read
- Auth-protected notification endpoints

---

## Architecture

The backend uses a layered structure to separate HTTP concerns, business logic, persistence, reusable infrastructure, and DTO/entity definitions.

```text
┌───────────────────────────────┐
│        React Frontend         │
│   TypeScript + Vite + Axios   │
└───────────────┬───────────────┘
                │ HTTPS / REST
                │ SignalR
                ▼
┌───────────────────────────────┐
│        TaskTracker.API        │
│ Controllers · Auth · Hubs     │
└───────────────┬───────────────┘
                ▼
┌───────────────────────────────┐
│    TaskTracker.Bussiness      │
│ Application / business logic  │
└───────────────┬───────────────┘
                ▼
┌───────────────────────────────┐
│ DataAccess / Core / Entities  │
│ EF Core · Repositories · DTOs │
└───────────────┬───────────────┘
                ▼
┌───────────────────────────────┐
│          PostgreSQL           │
└───────────────────────────────┘
```

### Main projects

- `TaskTracker.API` — HTTP API, authentication configuration, SignalR hub integration
- `TaskTracker.Bussiness` — application and business logic
- `TaskTracker.DataAccess` — data-access implementation
- `TaskTracker.Core` — shared infrastructure, persistence abstractions and security utilities
- `TaskTracker.Entities` — entities and DTOs
- `tasktracker-client` — React + TypeScript frontend

---

## Production Topology

```text
                       Internet
                          │
                        HTTPS
                          │
                          ▼
                  ┌─────────────┐
                  │    Nginx    │
                  └──────┬──────┘
                         │
             ┌───────────┴───────────┐
             │                       │
             ▼                       ▼
   React production build     ASP.NET Core API
   /var/www/canncodehub       systemd-managed
                                     │
                                     ▼
                                PostgreSQL
```

The public site and API are served through separate Nginx server blocks, with the API reverse-proxied to the ASP.NET Core process.

---

## Database Migrations

Database schema changes are managed with Entity Framework Core migrations.

Example local command:

```bash
dotnet ef database update \
  --project TaskTracker.Core \
  --startup-project TaskTracker.API \
  --context TaskTrackerDbContext
```

Production migrations are treated as a deployment operation rather than being applied automatically on application startup.

---

## Local Development

### Requirements

- .NET 10 SDK
- Node.js / npm
- PostgreSQL

### Backend

Restore dependencies:

```bash
dotnet restore TaskTracker.slnx
```

Configure local secrets without committing credentials to the repository. Important configuration keys include:

```text
ConnectionStrings:PostgreSql
TokenOptions:SecurityKey
Smtp:Host
Smtp:Port
Smtp:User
Smtp:Pass
Smtp:FromEmail
PasswordRecovery:HmacSecret
```

`PasswordRecovery:HmacSecret` must be at least 32 characters.

Apply migrations:

```bash
dotnet ef database update \
  --project TaskTracker.Core \
  --startup-project TaskTracker.API \
  --context TaskTrackerDbContext
```

Run the API:

```bash
dotnet run --project TaskTracker.API
```

### Frontend

```bash
cd tasktracker-client
npm install
npm run dev
```

Production frontend build:

```bash
npm run build
```

---

## Why This Project Exists

TaskTracker is being developed as a long-running portfolio project focused on the parts of software engineering that appear after the first CRUD endpoints are finished.

The project is used to practice and demonstrate:

- Backend architecture
- Authentication and account recovery
- Authorization
- Relational data modelling
- Secure token workflows
- Real-time communication
- Collaboration workflows
- Frontend/backend integration
- Production database migrations
- Linux service management
- Nginx reverse proxy configuration
- Release deployment and rollback thinking

The goal is to keep evolving the same system instead of replacing it with a sequence of disconnected tutorial projects.

---

## Current Roadmap

Planned next-stage improvements include:

- Public landing and onboarding flow
- Improved dashboard experience
- Task search, filtering and sorting
- Activity history / audit-style timeline
- Collaboration UX improvements
- Automated tests
- CI/CD
- Better observability and structured logging
- Security and infrastructure hardening
- AI-assisted task-management features

---

## Project Status

**Active development / production deployed**

TaskTracker currently has a working production environment and is expanded incrementally through focused feature branches.

Some infrastructure and security hardening work is intentionally tracked separately from product features, and the roadmap will continue to change as the application matures.

---

## Author

**Can Engin Çizmeci**  
Computer Engineering Student  
Backend & AI-focused Developer

GitHub: https://github.com/canengincizmeci
