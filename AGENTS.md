# Repository Guidelines

## Project Structure & Module Organization
This repository is a design-first project for a voice interaction system that combines streaming ASR, Agent orchestration, and TTS.

Main areas:
- `docs/`: source of truth for architecture, flow, and acceptance criteria (ASR pipeline, text post-processing, two-pass strategy).
- `referense-code/`: reference-only `.NET` examples for implementation ideas. This folder is not the production project baseline.

When contributing, prioritize updates in `docs/` to keep system decisions and module boundaries explicit.

## Build, Test, and Development Commands
Run from repository root:

- `rg --files docs`: quickly inspect current design docs.
- `rg "ASR|TTS|Agent|two-pass" docs`: verify terminology consistency across documents.
- `dotnet build referense-code/XiaoZhi.Net.Test/XiaoZhi.Net.Test.csproj` (optional): validate ideas against reference code only.

## Coding Style & Naming Conventions
- Documentation language can be Chinese or English, but keep one language per file.
- Use clear headings and short sections; prefer checklists for acceptance criteria.
- Keep terms stable: use `ASR`, `Agent`, `TTS`, `streaming`, and `two-pass` consistently.
- If editing `referense-code/`, follow C# defaults: 4-space indentation, PascalCase for types/methods, camelCase for locals.

## Testing Guidelines
Testing is currently design validation first, code validation second.

For design/document changes:
- ensure related docs stay aligned (architecture, process, acceptance checklist);
- include at least one concrete flow example (input -> ASR -> Agent -> TTS output);
- update acceptance points in `docs/ASR验收检查清单.md` when behavior expectations change.

For reference code changes (optional scope):
- build and run the relevant sample project;
- describe what was validated and what remains unverified.

## Commit & Pull Request Guidelines
Current history is minimal (single `Initial commit`), so use clear, imperative commit subjects:
- `Refine two-pass ASR interaction flow`
- `Add TTS fallback acceptance criteria`

PRs should include:
- what changed and why;
- affected paths (for example `docs/2pass方案描述.md`);
- validation steps (doc cross-checks, optional reference-code build/run);
- impact on voice pipeline behavior, latency, or output quality.
