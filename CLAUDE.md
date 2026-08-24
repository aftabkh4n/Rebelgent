# CLAUDE.md — Rebelgent Development Rules

These rules apply to every Claude Code session working on the Rebelgent repository.
They are permanent and take precedence over any session-level preferences.

---

## Git Safety

1. Never commit directly to `main`.
2. Never merge a pull request.
3. Never force push shared branches.
4. Never rewrite Git history unless explicitly authorized by the repository owner.
5. Future agent-generated work must happen on isolated task branches or worktrees under `D:\Projects\_RebelgentWorkspaces`.

## Release Safety

6. Never publish a NuGet package without explicit human approval.
7. Never create a production release without explicit human approval.
8. Never deploy to production without explicit human approval.

## Infrastructure Safety

9. Never delete infrastructure without explicit approval.
10. Never delete persistent data without explicit approval.
11. Never modify secrets without explicit approval.
12. Never expose credentials, tokens, connection strings, API keys, or private information in source code or logs.

## Development Rules

13. Every behavioral code change requires appropriate tests.
14. Run `dotnet build` before declaring work complete.
15. Run `dotnet test` before declaring work complete.
16. A task is not complete if tests fail.
17. Fix warnings introduced by your own changes where reasonable.
18. Use nullable reference types.
19. Use async correctly — do not use `.Result` or `.Wait()` on tasks.
20. Use `CancellationToken` for long-running or external operations.
21. Prefer clear explicit code over clever code.
22. Keep methods focused.
23. Keep names descriptive.
24. Avoid unnecessary abstractions.
25. Prefer interfaces at real architectural or integration boundaries rather than creating interfaces for every class.

## Architecture Rules

26. Rebelgent.Core must remain provider-independent.
27. Claude-specific code must not enter Rebelgent.Core.
28. Microsoft Agent Framework-specific code must not enter Rebelgent.Core.
29. Google ADK-specific code must not enter Rebelgent.Core.
30. Ollama-specific code must not enter Rebelgent.Core.
31. Telegram-specific code must not enter Rebelgent.Core.
32. GitHub-specific code must not enter Rebelgent.Core.
33. MCP-specific code must not enter Rebelgent.Core.
34. External providers must be implemented through adapter or infrastructure projects (e.g. Rebelgent.ClaudeCode, Rebelgent.Telegram).

## Requirement Rules

35. Do not invent requirements.
36. If a requirement is ambiguous and materially affects architecture, stop and report the ambiguity.
37. Preserve backward compatibility unless explicitly authorized otherwise.
38. Do not silently change public behavior.
39. Do not over-engineer for hypothetical future requirements.

## Agent Behavior Rules

40. Developer agents never review or approve their own work.
41. QA agents should actively attempt to find failures and regressions, not confirm success.
42. Reviewer agents should remain independent from the implementation agents they review.
43. Agent execution must be auditable — every execution should be traceable.
44. Human approval gates must never be bypassed.
45. Public content must not be automatically published until explicitly enabled by the repository owner.

---

## Human Approval Gate Reference

### Actions agents may eventually perform automatically

- Read repository
- Analyze code
- Read issues
- Create tasks
- Run builds
- Run tests
- Create branches
- Modify isolated task branches
- Create documentation drafts
- Prepare pull requests
- Perform QA
- Perform code review
- Generate content drafts

### Actions that always require explicit human approval

- Merge to main
- Production deployment
- NuGet package publishing
- Public content publishing
- Public replies made in the user's identity
- Destructive infrastructure operations
- Persistent data deletion
- Secret modification
- High-risk production actions

---

## Writing Style Policy (for future writing agents)

Generated articles and posts must:

- Sound naturally human-written
- Use concrete facts from actual code, commits, issues, tests, and releases
- Never invent experiences
- Never invent download counts or metrics
- Never invent benchmarks
- Avoid generic AI marketing language
- Avoid repetitive conclusions
- Avoid excessive headings
- Avoid excessive bullet lists
- Use varied sentence lengths
- Preserve the author's personal developer voice
- Discuss real mistakes and tradeoffs where relevant
- Use straightforward punctuation
- Never use em dashes
- Avoid stylistic patterns that make content obviously AI-generated

Public posting of generated content requires human approval.

---

## Project Structure Reference

```
D:\Projects\Rebelgent\          ← this repository
D:\Projects\_RebelgentWorkspaces\  ← future isolated agent workspaces
```

Agent workspaces are created per-task and per-role. They are never committed to this repository.
