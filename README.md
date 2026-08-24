# Rebelgent

Rebelgent is an open-source AI software-house orchestrator that coordinates specialized agents to plan, build, test, review, release, document, and maintain software projects.

> **Status:** Experimental. Under active development. Milestone 1 — foundation and architecture — is complete. Autonomous software development is not yet implemented.

---

## What It Is

Rebelgent acts as the governance and orchestration layer for a team of specialized AI agents. Rather than a single AI assistant that does everything, Rebelgent coordinates agents that each have a defined role: architect, developer, QA engineer, code reviewer, security reviewer, release manager, and others.

The system is designed around a few non-negotiable principles:

- **Provider independence.** The core domain has no dependency on Claude, OpenAI, Microsoft Agent Framework, Google ADK, or any other AI runtime. Providers are adapters.
- **Human approval gates.** Merging code, deploying, publishing packages, and other consequential actions require explicit human approval. Agents prepare the work; humans authorize the outcome.
- **Auditable execution.** Every agent execution is recorded with a summary, artifacts, and outcome.
- **Isolated workspaces.** Each agent role works in its own git worktree, preventing context bleed between developer, QA, and reviewer agents.

---

## Architecture (Intended)

```
User
  |
  v
Telegram
  |
  v
Rebelgent API
  |
  v
Engineering Manager
  |
  +----------------+----------------+
  |                |                |
Architect      Developer           QA
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

## Current State (Milestone 1)

Milestone 1 establishes the foundation only:

- Solution structure with 5 source projects and 2 test projects
- Core domain: `AgentTask`, `AgentRole`, `AgentTaskStatus`, `RiskLevel`
- Approval model: `ApprovalRequest`, `ApprovalType`, `ApprovalDecision`
- Agent abstractions: `IRebelAgent`, `IAgentRegistry`, `AgentExecutionResult`
- Task lifecycle service with explicit, validated status transitions
- Minimal ASP.NET Core API with `/health` and `/api/system/info`
- 30+ unit tests

Not yet implemented: Telegram, GitHub, any AI provider, database, agent execution.

---

## Project Structure

```
src/
  Rebelgent.Core/          Domain models, lifecycle rules
  Rebelgent.Contracts/     API DTOs
  Rebelgent.Agents/        Agent interfaces and registry
  Rebelgent.Infrastructure/ DI composition
  Rebelgent.Api/           ASP.NET Core Web API

tests/
  Rebelgent.Core.Tests/
  Rebelgent.Agents.Tests/

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

---

## Contributing

See [CLAUDE.md](CLAUDE.md) for the development rules that apply to this project.
