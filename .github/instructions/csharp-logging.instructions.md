---
description: 'Log level rules for C# code in this repository (Serilog).'
applyTo: '**/*.cs'
---

# Logging standard

All logging goes through Serilog's static `Log`. The level is not a matter of taste - it says what
kind of event this is. Pick it by answering: *did something fail, and was that failure expected?*

## Levels

| Level | Use it for |
| --- | --- |
| `Log.Fatal` | The application cannot continue and is about to end. |
| `Log.Error` | An **unexpected** error, i.e. an exception the code could not handle meaningfully. |
| `Log.Warning` | Something could not be done, the failure is a **foreseen** one, and the application carries on. |
| `Log.Information` | Something could not be done because it does not apply or is not available here - and the outcome that a requested change **was** applied. |
| `Log.Debug` | Full debugging detail: the concrete data behind a step, dumped for later analysis. |
| `Log.Verbose` | The flow of the processing - "we did this, then this" - for following a run in test mode. |

### The distinction that matters

- **Expected inability → `Information`.** The operation was never possible in this environment:
  an optional tool is not installed, the platform has no such feature, the setting was already in the
  wanted state and nothing had to be written. Nothing is wrong.
- **Expected failure → `Warning`.** It was attempted and it did not work, but this is a failure mode
  the code anticipates and recovers from: a process vanished between listing and signalling, the
  desktop refused to open a link. The user's intent was not fulfilled.
- **Unexpected error → `Error`.** An exception reached a catch block that exists to keep the
  application alive, not because this specific failure was foreseen. Always pass the exception as the
  first argument: `Log.Error(ex, "...")`.

## Rules

- Always pass the exception object when logging inside a `catch`: `Log.Warning(ex, "...")`, not
  `Log.Warning("... {Message}", ex.Message)`. Serilog renders the stack trace itself.
- Use message templates with named placeholders (`{Count}`, `{Path}`), never string interpolation.
  Interpolation destroys the structured properties that make the log searchable.
- Do not log and rethrow the same failure: the layer that handles it logs it, once.
- Do not log an exception at `Error` when the code deliberately continues with a fallback - that is a
  `Warning` or, if the fallback is the normal case, an `Information`.
- A message must stand on its own. `Log.Debug` and `Log.Warning` are *above* `Log.Verbose` in Serilog's
  order, so a run configured for `Debug` shows them **without** the surrounding `Verbose` flow lines.
  Never write a message that only makes sense together with a `Verbose` line.
- `Log.Verbose` is where per-item and per-line noise belongs (each process examined, each line a tool
  printed). Never put that in `Information`.
- Say what was affected, not only that something happened: include the name, path, id or count.
