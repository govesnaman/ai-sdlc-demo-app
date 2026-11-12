---
name: structured-logging
description: "Record auditable AI task activity in ai-interaction-log.md. Use at the end of every AI-assisted task or when recording agent, prompt, summary, and changed files."
---

# Structured AI Interaction Logging

At the end of every task that changes files or performs a substantive repository operation, append a structured entry to the root `ai-interaction-log.md`.

## Procedure

1. Identify the agent name, task step, a concise prompt description, a truthful summary, and all files changed during the task.
2. On a fresh clone, enable the tracked Git commit hook once from the repository root:

   ```sh
   git config core.hooksPath .githooks
   ```

3. From the repository root, invoke:

   ```sh
   node scripts/log-ai-interaction.js \
     --agent "coder-agent" \
     --step "implementation" \
     --prompt "Implement the requested approval behavior" \
     --summary "Implemented the domain rule and added focused tests." \
     --files "backend/src/InvoiceApp.Domain/ApprovalService.cs
   backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs"
   ```

4. Replace example values with task-specific details. Keep `--files` as a newline-separated list inside one quoted argument; use an empty string only when no repository files changed.
5. Verify the command succeeds and that the entry appears in `ai-interaction-log.md`.
6. Never include credentials, tokens, customer data, or sensitive prompt content. Summarize such content safely rather than copying it to the audit log.

The Git `prepare-commit-msg` hook also appends a commit-preparation record and stages the updated log. Keep the task-level entry descriptive; the hook record supplements it and does not replace it.

## Entry Format

The logging CLI writes timestamped Markdown entries with agent, step, prompt summary, outcome summary, and changed file paths. If the CLI is unavailable, append a block with the same fields and an ISO-8601 timestamp, preserving all prior entries.
