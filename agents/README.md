# Agent Definitions

This directory will contain agent definitions for each specialized role in the Rebelgent software house.

Agent definitions are not yet implemented. This directory is reserved for Milestone 2+.

---

## Planned Structure

```
agents/
  engineering-manager/
  architect/
  developer/
  qa/
  reviewer/
  security/
  devops/
  writer/
```

Each agent directory may eventually contain:

```
AGENT.md      — role description, capabilities, constraints
skills/       — reusable skill definitions
policies/     — behavioral policies and approval rules
```

## Notes

- Agent implementations reference `IRebelAgent` from `Rebelgent.Agents`
- Provider-specific execution (Claude Code, Ollama, etc.) lives in adapter projects, not here
- Each agent runs in an isolated worktree during task execution
- Agents do not review or approve their own work
