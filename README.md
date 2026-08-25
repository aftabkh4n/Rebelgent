# Rebelgent

Rebelgent is an open-source AI software-house orchestrator that coordinates specialized agents to plan, build, test, review, release, document, and maintain software projects.

> **Status:** Experimental. Under active development. Milestone 2 — Telegram integration and SQLite persistence — is complete. Autonomous software development is not yet implemented.

---

## What It Is

Rebelgent acts as the governance and orchestration layer for a team of specialized AI agents. Rather than a single AI assistant that does everything, Rebelgent coordinates agents that each have a defined role: architect, developer, QA engineer, code reviewer, security reviewer, release manager, and others.

The system is designed around a few non-negotiable principles:

- **Provider independence.** The core domain has no dependency on Claude, OpenAI, Microsoft Agent Framework, Google ADK, or any other AI runtime. Providers are adapters.
- **Human approval gates.** Merging code, deploying, publishing packages, and other consequential actions require explicit human approval. Agents prepare the work; humans authorize the outcome.
- **Auditable execution.** Every agent execution is recorded with a summary, artifacts, and outcome.
- **Isolated workspaces.** Each agent role works in its own git worktree, preventing context bleed between developer, QA, and reviewer agents.

---

## What Rebelgent Can Do Now (Milestone 2)

- Receive task requests from Telegram
- Create `AgentTask` records
- Persist tasks to a local SQLite database
- Return task confirmation through Telegram
- List recent tasks via `/tasks`
- Show system status via `/status`
- Expose tasks via REST API (`POST /api/tasks`, `GET /api/tasks/{id}`, `GET /api/tasks`)

Agent execution is not yet implemented. Tasks are recorded and confirmed, but no code is written or reviewed automatically.

---

## Architecture

### Implemented (Milestone 2)

```
User
  |
  v (Telegram message)
Telegram Adapter
  |
  v
TelegramUpdateHandler
  |
  v (authorized users only)
ITaskService
  |
  v
AgentTask (domain)
  |
  v
IAgentTaskRepository
  |
  v
SQLite (via EF Core)
  |
  v (confirmation back through Telegram)
User
```

### Planned (future milestones)

```
Rebelgent
  |
  v
Engineering Manager
  |
  +------------------+------------------+
  |                  |                  |
Architect         Developer            QA
                     |
                 Claude Code
                     |
                   GitHub
                     |
               Code Reviewer
                     |
             Human Approval
                     |
              Merge / Release
```

---

## Project Structure

```
src/
  Rebelgent.Core/          Domain models, lifecycle rules, repository interfaces
  Rebelgent.Contracts/     API request/response DTOs
  Rebelgent.Agents/        Agent interfaces and registry
  Rebelgent.Infrastructure/ Core service implementations (TaskService)
  Rebelgent.Persistence/   EF Core SQLite adapter
  Rebelgent.Telegram/      Telegram bot adapter
  Rebelgent.Api/           ASP.NET Core Web API

tests/
  Rebelgent.Core.Tests/
  Rebelgent.Agents.Tests/
  Rebelgent.Persistence.Tests/
  Rebelgent.Telegram.Tests/

docs/
  architecture.md

agents/
  README.md
```

---

## Getting Started

Requirements: .NET 10 SDK

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Rebelgent.Api
```

The database (`data/rebelgent.db`) is created automatically on first run.

---

## Local Development Setup

### Run without Telegram

The API runs without Telegram configured. Telegram is disabled by default.

```bash
dotnet run --project src/Rebelgent.Api
```

Endpoints available:
- `GET /health`
- `GET /api/system/info`
- `POST /api/tasks`
- `GET /api/tasks/{id}`
- `GET /api/tasks?count=20`

### Enable Telegram (optional)

1. Create a bot with [@BotFather](https://t.me/BotFather) and obtain your token.

2. Find your numeric Telegram user ID (send a message to @userinfobot or similar tool — use the numeric ID, not a username).

3. Initialise user-secrets in the API project:

```bash
dotnet user-secrets init --project src/Rebelgent.Api
```

4. Set the bot token and your user ID:

```bash
dotnet user-secrets set "Telegram:Enabled" "true" --project src/Rebelgent.Api
dotnet user-secrets set "Telegram:BotToken" "YOUR_BOT_TOKEN_HERE" --project src/Rebelgent.Api
dotnet user-secrets set "Telegram:AllowedUserIds:0" "YOUR_NUMERIC_USER_ID_HERE" --project src/Rebelgent.Api
```

Replace `YOUR_BOT_TOKEN_HERE` and `YOUR_NUMERIC_USER_ID_HERE` with your actual values.

**Never commit the token or user ID to source control.**

5. Run:

```bash
dotnet run --project src/Rebelgent.Api
```

6. In Telegram, send `/start` to your bot. Try sending a task:

```
Add memory expiration support to BlazorMemory
```

Then check the result with `/tasks`.

---

## Contributing

See [CLAUDE.md](CLAUDE.md) for the development rules that apply to this project.
