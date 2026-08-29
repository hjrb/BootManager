# Contributing to BootManager

Thanks for taking the time. This page describes how a change gets from your machine into a
release, and what the automation expects from you.

By contributing you agree that your contribution is licensed under the
[Apache License 2.0](LICENSE), like the rest of the project.

---

## Before you start

- For a bug, open an [issue](https://github.com/hjrb/BootManager/issues/new/choose) first, so
  the problem is on record even if the fix takes a while.
- For a larger feature, open an issue before writing code. It saves you from building something
  that does not fit the project.
- Found a security weakness? Do not open an issue — follow [SECURITY.md](SECURITY.md).

---

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Nothing else.

```powershell
dotnet build BootManager.csproj -c Release -warnaserror
```

`-warnaserror` is what CI uses, so a build that is clean here will not fail there.

To produce the downloadable builds for every platform:

```powershell
.\publish.ps1 -Clean
```

Cross-compiling works from any of the three operating systems — the compiler only needs the
target's runtime package, which NuGet downloads on its own. Only *testing* a build requires the
matching machine.

---

## Testing a change

BootManager talks to firmware, so most defects cannot be caught by reading the diff. Before
opening a pull request, run the affected code path on at least one real machine and say in the
pull request which one. If the change touches platform-specific code under `Services`, it has to
be tried on that platform.

Take care with anything that writes: `Set as Default` and `Set as Next Boot` change how your
computer starts. Note down your current boot order before experimenting, so you can put it back.

---

## Writing the code

- Follow the existing style of the file you are editing.
- Comments follow
  [.github/instructions/csharp-comments.instructions.md](.github/instructions/csharp-comments.instructions.md):
  say *why*, not *what*, and keep it short.
- Never build a shell command by pasting strings together. External tools are invoked through
  `ProcessRunner` with an argument list, which is what keeps boot entry names from being
  interpreted as commands.
- Keep the graphical window and the command line in sync. A new capability should be reachable
  from both, and `Commands.md` documents the command line.
- Do not commit anything from `bin`, `obj`, `publish`, or `logs`. They are ignored on purpose.
- Do not commit log files or firmware output from your own machine: they identify your hardware.

---

## Opening a pull request

`main` is protected: it cannot be pushed to, and every change arrives through a pull request.

1. Fork the repository, or create a branch if you have write access.
2. Commit your work with a message that says what changed and why.
3. Open a pull request against `main` and fill in the template.
4. Wait for `Build` and `Analyze C#` to turn green. They are required and a red check blocks the
   merge button.
5. Address review comments by pushing more commits; do not force-push while a review is open.

Pull requests are merged with squash or rebase, so `main` keeps a linear history.

---

## Releasing (maintainers)

Releases are produced by [.github/workflows/release.yml](.github/workflows/release.yml), never by
hand.

```powershell
git tag v1.2.3
git push origin v1.2.3
```

The workflow builds all seven targets, packages them, publishes a release with a
`SHA256SUMS.txt`, and — only after the upload succeeded — deletes every older release together
with its tag. That is deliberate: the account has little storage, so exactly one release is
kept. Anyone who needs an older build can rebuild it from its commit.

A release can also be started from the Actions tab, where the version is typed in by hand.

---

## Repository setup (maintainers)

These settings live on GitHub, not in the repository, and have to be applied once.

**Branch protection.** Import [.github/rulesets/protect-main.json](.github/rulesets/protect-main.json)
under *Settings → Rules → Rulesets → New ruleset → Import a ruleset*, or apply it with the CLI:

```bash
gh api --method POST repos/hjrb/BootManager/rulesets --input .github/rulesets/protect-main.json
```

It blocks direct pushes and force-pushes to `main`, blocks deleting the branch, requires a pull
request whose `Build` and `Analyze C#` checks passed against the current head of `main`, and
grants no bypass to anyone, including administrators. The required approval count is `0` so that
a single maintainer is not locked out — raise it to `1` as soon as there is a second person who
can review.

**Other settings to check once the repository is public:**

| Where | Setting |
| --- | --- |
| Settings → General → Pull Requests | Allow squash and rebase merges only; enable *Automatically delete head branches* |
| Settings → Actions → General | Workflow permissions: *Read repository contents*; require approval for all outside contributors' workflow runs |
| Settings → Actions → General | Disable *Allow GitHub Actions to create and approve pull requests* |
| Settings → Security and quality → Advanced Security | Enable *Secret Protection*, then *Push protection* inside it |
| Settings → Security and quality → Advanced Security | Enable *Private vulnerability reporting*, *Dependabot alerts*, and *Dependabot security updates* |

Before the repository is switched to public, check the history for anything that should not be
there — log files, firmware dumps, machine names, tokens. Making a repository public exposes
every commit that was ever pushed, not only the current files.
