# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Candidate Screening Loader

A Windows desktop app that batch-processes the CVs of a job opening against the AI Hub's
`serenityapp-candidate-screening-studio` agent. It exists because the Candidate Screening Studio micro app
accepts 5 files per run, and Human Resources needs to process hundreds.

It doesn't modify the agent, the dataset or the micro app: it's just another client of the same API. The agent
writes the candidate rows itself, and the micro app shows them.

The vocabulary the user sees **is not the code's**: a `Batch` never appears on screen, and a job opening is
called *oferta*.

## Commands

> **Windows / PowerShell.** When running through a tool, use the **PowerShell tool** — the Bash one runs
> `/usr/bin/bash` and silently drops `$env:` assignments.

```powershell
# Build — order matters: the interface is embedded in the executable
pnpm -C ui build
dotnet build src/ScreeningLoader.sln

# Publish the distributable executable
pnpm -C ui build
dotnet publish src/ScreeningLoader.Shell -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Development: the shell points to the dev server, with hot reload of the interface
pnpm -C ui dev
```

## Architecture

Three pieces, one repo:

- **`src/ScreeningLoader.Core`** — the engine. No UI, no window dependencies. It exposes a run as a stream of
  `RunEvent` that any host consumes, and `ScreeningLoaderEngine` as its only public surface.
- **`src/ScreeningLoader.Shell`** — the window. A WinForms `Form` fully covered by a WebView2 control, plus the
  `PostWebMessage` bridge. **Nothing from WinForms is visible**: the frame exists for the border, the title and
  the icon, and the only other thing used from the library is the native folder dialog.
- **`ui/`** — the React interface, built on Serenity's design system.

**`src/ScreeningLoader.Console`** is the engine's development host. It isn't distributed.

## The interface

**The dark theme class is `serenity-dark`, not `dark`** — that's how the tokens' `@custom-variant` defines it.
Using `dark` doesn't visibly break anything; it simply does nothing.

## Errors

Every engine exception derives from `ScreeningLoaderException` and **declares its `ErrorKind`**. The class of
an error is a property of the error, not of the place where it's caught:

- **`Transient`** — retried with backoff. 429, dataset lock, network timeout.
- **`File`** — that CV goes to `fallidos/` and the run continues. Unreadable, not a CV, its INSERT failed.
- **`Fatal`** — the run aborts. Credentials, missing Execution permission, agent without configuration.

A `catch` doesn't decide policy: it reads it from the `ErrorKind`. Without this rule, the difference between
"retry" and "discard this CV" ends up settled case by case, which is how a candidate gets lost silently.

## Personal data

CVs are personal data of real candidates. Two rules that aren't negotiable:

**Never log the SQL statement or the body of the dataset's response.** The message of a failing INSERT carries
the whole row: name, email, phone and the full observations. Log the operation and the status, never the
content — see `ErrorLog.Describe`.

**The audit log carries no candidate data.** File name, result, score and time. Never the person's name, their
email, their phone or a single line of their observations.

## The dataset

It's accessed only through the plugin's skill, and the plugin has rules of its own:

- **One statement per call.** There's no batching and no transactions.
- **Never `SELECT *`** — it prepends an internal column and returns the rest as usual. Columns are named.
- **The table names are `Jobopenings` and `Candidates`**, with that exact capitalization. The logical name
  `job_openings` doesn't exist: the plugin flattens it, and a query against it finds nothing.
- **Datetimes come back as `"2026-08-24 09:05:41"`**, plain text without a time zone.
- **A query with no matches returns 200 with empty `rows`**, not 404 — verified against the Hub. The skills
  endpoint's 404 means the skill code doesn't exist, so **it isn't translated into an empty list**: doing so
  would hide a wrong `datasetSkillCode` behind "this organization has no job openings". The micro app does
  have that translation; on the direct path to the Hub that branch never fires.
- **A malformed statement returns 500**, not 400, and the body carries the statement inside. A 500 is treated
  as transient, so a broken query is retried until the attempts run out before failing.

Every SQL literal is built with `Sql.*`. Identifiers are never interpolated; the helpers throw instead of
coercing.

**The dataset is SQLite: one writer at a time.** That's why batches are sequential by default.

## The platform

- **An execution's receipt doesn't carry the file name.** The file ↔ row mapping **is queried**
  (`SELECT id, cv_file_name FROM Candidates WHERE id IN (...)`), never inferred by counting. Inferring it
  archives CVs that have no row and leaves pending the ones that do.
- **`processEmbeddings` is always sent explicitly.** Three different defaults are in play: the Hub's direct
  endpoint uses `false`, Nexus's uses `true`, and the official SDK `true`.
- **An upload returns 200 before the file is processed.** Only the ones that reach `success` go on to an
  execution; an unprocessed one produces a candidate built on an empty document.
- **The JWT lasts 15 minutes**, and a run lasts much longer. It's refreshed ahead of time, before each batch,
  not in reaction to a 401.
- **The official SDK `SubgenAI.SerenityStar.SDK` can't be used here**: it requires an API key on every
  construction path, and this app authenticates with username and password. It does work as a reference for the
  wire shapes.

## Comments

Comment sparingly. Most code reads without comments: good names and a clear structure carry the intent. A
comment is justified only when the code can't explain itself: a non-obvious *why*, a constraint, a trap, or a
decision a reader would question.

Write each comment for **someone reading the diff in a PR** who wasn't part of the conversation that produced
the code.

- **None by default.** Before adding one, assume it isn't needed.
- **Explain the *why*, never the *what*.** Don't narrate what the next line does.
- **One line, plain language.** No multi-line stories or conversational tone.
- **No conversation residue.** Never reference the prompt, a previous attempt, a discarded alternative, or "we
  decided…". Nor design documents: the comment describes the code as it is, not how it got there.
- **No dividers or section narration.** If a block needs a title, extract a function with a good name.
- **Delete commented-out code.**

XML doc summaries describe the member's role and nothing else.

**Language:** comments, XML docs and internal messages (logs, exceptions that never reach the screen, the
development console) are written in English. Text the user sees — screens, and the messages that reach them —
is in Spanish, because the app is used by a Spanish-speaking HR team.
