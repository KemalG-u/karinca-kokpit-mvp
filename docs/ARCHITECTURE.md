# Architecture

Interpretation: a short map written from the code, not a specification.

| File | Role |
|---|---|
| `src/App.cs` | Entry point (`Main`, command-line check modes, single-instance mutex), capsule, management panel, theme, keyboard handling. |
| `src/Radial.cs` | The radial wheel: geometry, hit-testing, hover/selection, sub-wheel and paging, drawing (also used by `--render-wheel`). |
| `src/Core.cs` | State and tools: JSON state (version 1, atomic replace, backup), tool validation (absolute local path only; shell scripts, URLs, relative, drive-relative and network paths are rejected), preparation of the workspace and notes file, launching a tool with a properly quoted working directory. |
| `src/CoreChecks.cs`, `src/UiChecks.cs`, `src/RadialChecks.cs` | The built-in checks behind `--check`, `--ui-check`, `--wheel-check`, `--context-check`. |
| `src/LaunchProbe.cs` | A tiny helper program the checks start, to prove that a launched tool gets the prepared working directory. |

Design rules visible in the code: tools run only after an explicit click; no network; a bad state file is preserved, not deleted; the cockpit never closes programs it did not start.
