---
description: 'Commenting and documentation standard for C# code in this repository.'
applyTo: '**/*.cs'
---

# Commenting and documentation standard

Write code that a competent developer who is **not** a C# expert can read and understand without
having to look anything up. Comments explain *why*, the code shows *what*.

## XML documentation comments

- Every **public** type, method, property and event gets an XML doc comment (`/// <summary>`).
- Also document `internal` members that carry non-obvious behaviour or platform assumptions.
- A `<summary>` states the **purpose** of the member, not a restatement of its signature.
  - Bad: `/// <summary>Sets the next boot entry.</summary>` on `SetNextBootEntryAsync`.
  - Good: explains that it is a *one-time* override that the firmware consumes and discards.
- Use `<param>` for parameters whose meaning, units, valid values or side effects are not obvious.
- Use `<returns>` when the return value needs interpretation (e.g. `null` has a special meaning).
- Use `<exception cref="...">` for exceptions the caller is expected to handle.
- Use `<remarks>` for background: design decisions, platform quirks, privilege requirements, and
  approaches that were deliberately rejected (and why).

## Explain intention, reasoning and assumptions

State the things that the code cannot express by itself:

- **Why** this approach was chosen, and which alternative was rejected and for what reason.
- **Assumptions** the code relies on (output formats, ordering, locale behaviour, privilege level).
  If an assumption might silently break, say what breaks.
- **Preconditions**, such as required Administrator/root rights or a required OS.
- Domain background a reader may not have (e.g. what a UEFI `BootNext` variable is).

## Explaining external processes and native APIs

This project drives the system mainly through command line tools and Win32 APIs. Every invocation
must be understandable without consulting the tool's manual:

- Document the command **and each individual option** you pass, in plain words.
  ```csharp
  // efibootmgr -n 0003
  //   -n <id>  sets "BootNext": the firmware boots this entry once on the next start and then
  //            clears the variable, so the normal boot order applies again afterwards.
  ```
- Explain the meaning of relevant **exit codes** and of output that is parsed.
- When parsing tool output, document the expected format and which parts are stable
  (e.g. localized vs. not localized) - this is an assumption that can break silently.
- For P/Invoke, document the native function, the meaning of its flags and constants, and any
  documented quirks (for example: an API that returns success even when it did nothing).

## Inline comments

- Comment non-obvious blocks, not individual trivial lines. Never narrate the obvious
  (`// increment i`).
- Prefer one clear sentence over a paragraph.
- Where a modern or terse C# construct is used (pattern matching, collection expressions, LINQ
  chains, `record` types), add a short note if its behaviour is not self-evident to a reader coming
  from another language.
- Keep comments truthful: when you change code, update the comment in the same edit.
