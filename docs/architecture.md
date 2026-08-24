# Rebelgent Architecture

## Overview

Rebelgent is a provider-independent orchestration and governance layer for AI-powered software development. It coordinates specialized agents that together behave like a professional software organization: planning, building, testing, reviewing, releasing, and documenting software projects.

Rebelgent itself does not implement any AI. It defines the contracts, domain model, lifecycle rules, and approval boundaries that provider-specific adapters plug into.

---

## Long-Term System Flow

```
User
  |
  v
Telegram
  |
  v
Rebelgent Control Plane (API)
  |
  v
Engineering Manager / Orchestrator
  |
  +-----------------------------+-----------------------------+
  |             |               |                             |
  v             v               v                             v
Architect    Developer         QA                       Security Reviewer
                |
                v
           Claude Code
           (or other runtime)
                |
                v
             GitHub
                |
                v
           Code Reviewer
                |
                v
         Human Approval Gate
                |
                v
        Merge to main / Release
```

This is the intended long-term flow. As of Milestone 1, only the core abstractions and domain model exist. No AI provider, Telegram, or GitHub integration is implemented yet.

---

## Core Principles

**Provider independence.** `Rebelgent.Core` and `Rebelgent.Agents` contain no references to Claude, OpenAI, Microsoft Agent Framework, Google ADK, Ollama, Telegram, GitHub, EF Core, or MCP. All external providers are implemented in separate adapter projects. The orchestration logic must remain portable.

**Explicit lifecycle.** Task status transitions are governed by `TaskLifecycleService`. Agents cannot assign arbitrary statuses. Every transition is explicit and validated. Invalid transitions throw `InvalidTaskTransitionException`.

**Human approval gates.** Sensitive actions (merging to main, deploying, publishing packages, publishing public content, deleting data, modifying secrets) require explicit human approval before proceeding. These approvals will eventually be delivered through Telegram. No approval gate may be bypassed by an agent.

**Auditable execution.** Every agent execution produces an `AgentExecutionResult` that records success or failure, a summary, any errors, and the artifacts produced. This creates an audit trail.

**Independent QA and review.** Developer agents do not review or approve their own work. QA and reviewer agents operate with separate context and separate worktrees from the developer agents they assess.

---

## Project Structure

| Project | Responsibility |
|---------|---------------|
| `Rebelgent.Core` | Domain models, enums, exceptions, task lifecycle rules |
| `Rebelgent.Contracts` | API request/response DTOs |
| `Rebelgent.Agents` | `IRebelAgent` interface, registry, execution models |
| `Rebelgent.Infrastructure` | DI composition, future infrastructure adapters |
| `Rebelgent.Api` | ASP.NET Core API, composition root |

### Dependency Graph

```
Rebelgent.Api
  -> Rebelgent.Contracts
  -> Rebelgent.Agents
  -> Rebelgent.Infrastructure
     -> Rebelgent.Agents
        -> Rebelgent.Core
```

`Rebelgent.Core` has zero external dependencies. `Rebelgent.Contracts` has zero external dependencies.

---

## Future Provider Adapters

When provider integrations are implemented, they will live in separate projects:

| Project | Purpose |
|---------|---------|
| `Rebelgent.ClaudeCode` | Claude Code process execution adapter |
| `Rebelgent.MicrosoftAgentFramework` | Microsoft Agent Framework adapter |
| `Rebelgent.GoogleAdk` | Google Agent Development Kit adapter |
| `Rebelgent.Ollama` | Ollama local model adapter |
| `Rebelgent.OpenAI` | OpenAI adapter |
| `Rebelgent.Telegram` | Telegram bot and approval delivery |
| `Rebelgent.GitHub` | GitHub integration (issues, PRs, branches) |
| `Rebelgent.Mcp` | Model Context Protocol adapter |

None of these projects exist yet. They are future work.

---

## Agent Roles

| Role | Responsibility |
|------|---------------|
| EngineeringManager | Orchestrates work across all agents |
| ProductManager | Manages requirements and priorities |
| SolutionArchitect | Designs system architecture |
| BackendDeveloper | Implements backend code |
| FrontendDeveloper | Implements frontend code |
| AiEngineer | Implements AI/ML features |
| QaEngineer | Tests and validates changes |
| SecurityReviewer | Reviews for security issues |
| CodeReviewer | Reviews code quality |
| DevOpsEngineer | Manages infrastructure and deployments |
| DocumentationWriter | Creates technical documentation |
| ReleaseManager | Manages the release process |
| TechnicalWriter | Creates user-facing documentation |
| CommunityManager | Manages community engagement |

---

## Task Lifecycle

```
Created
  -> Planning
     -> AwaitingApproval
        -> Approved
           -> InProgress
              -> Testing
                 -> Reviewing
                    -> Completed
                    -> ChangesRequested
                          -> InProgress (retry)
              -> ChangesRequested
                    -> InProgress (retry)
```

Any active state may also transition to `Failed` or `Cancelled`. Completed, failed, and cancelled tasks are terminal — they cannot be restarted.

---

## Approval Model

`ApprovalRequest` records a pending human decision. Approval types:

| Type | Example |
|------|---------|
| Architecture | Approve a new architectural approach |
| Merge | Merge a pull request to main |
| Deployment | Deploy to a production environment |
| PackagePublish | Publish a NuGet package |
| ContentPublish | Publish an article or post |
| DestructiveAction | Delete data or infrastructure |
| SecretChange | Modify a secret or API key |

Decisions are `Approved` or `Rejected`. Once resolved, an approval request is immutable.

Telegram will eventually deliver approval requests to the repository owner and receive their decisions. This is not yet implemented.

---

## Isolated Agent Workspaces

Future agent work will use isolated git worktrees under:

```
D:\Projects\_RebelgentWorkspaces\
```

Example structure for a task on the BlazorMemory project:

```
D:\Projects\_RebelgentWorkspaces\
    blazormemory-issue-52-developer\
    blazormemory-issue-52-qa\
    blazormemory-issue-52-reviewer\
```

Each agent role gets its own worktree. This ensures:

- Developer context does not bleed into QA or review context
- QA agents test against a clean state, not the developer's in-progress working tree
- Reviewer agents see only the final changeset, not intermediate development state
- Multiple agents can work on separate tasks simultaneously without conflicts
- Workspaces are disposable and do not pollute the main repository

Workspaces are managed by the orchestration layer and are never committed to this repository.

---

## Human Approval Philosophy

Agents are trusted to perform read-only and isolated operations autonomously. Any action with external, persistent, or public consequences requires a human decision.

### Autonomous (no approval required)

- Read repository contents
- Analyze code
- Read issues and pull requests
- Create internal tasks
- Run builds and tests
- Create branches
- Modify isolated task branch contents
- Create documentation drafts
- Prepare pull request drafts
- Perform QA and code review
- Generate content drafts

### Requires human approval

- Merge to main
- Production deployment
- NuGet package publishing
- Public content publishing
- Public replies in the user's identity
- Destructive infrastructure operations
- Persistent data deletion
- Secret modification
- High-risk production actions

This boundary exists to keep humans in control of the outcomes while allowing agents to handle the preparation work autonomously.
