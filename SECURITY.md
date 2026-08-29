# Security policy

BootManager reads and writes UEFI firmware variables and needs administrator or root rights to
do so. A defect in it can therefore have consequences well beyond the program itself, up to a
machine that no longer boots. Reports are taken seriously.

## Supported versions

Only the latest release is supported. The release workflow keeps exactly one release, so the
newest download on the [releases page](https://github.com/hjrb/BootManager/releases) is always
the supported one.

## Reporting a weakness

Please **do not open a public issue** for anything that could be exploited.

Use [private vulnerability reporting](https://github.com/hjrb/BootManager/security/advisories/new)
instead. It creates a draft advisory that only the maintainers can read.

Helpful in a report:

- what an attacker gains, and what they need in order to get there
- the operating system, firmware vendor, and the build you used
- the smallest sequence of steps that reproduces the problem

You can expect a first reply within 14 days. If a fix is needed, it is released before the
advisory is published.

## Scope

In scope:

- privilege escalation through the elevation helper or through how external tools are invoked
- command or argument injection through boot entry names, file paths, or command line input
- writing firmware variables the user did not ask to be written
- the release pipeline and its published binaries

Out of scope:

- that the program requires administrator or root rights; that is by design and documented
- firmware defects of a particular vendor that BootManager only surfaces
- results from automated scanners without a demonstrated impact

## Verifying a download

Every release ships a `SHA256SUMS.txt`. Compare it against the file you downloaded before
running anything with elevated rights:

```bash
sha256sum --check --ignore-missing SHA256SUMS.txt
```

```powershell
Get-FileHash .\BootManager-1.2.3-win-x64.zip -Algorithm SHA256
```

The binaries are built by the `Release` workflow from the tagged commit in this repository and
are not signed with a code signing certificate, so Windows SmartScreen and macOS Gatekeeper will
warn about them.
