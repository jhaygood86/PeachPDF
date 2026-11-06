# Security Policy

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues, pull requests, or discussions.**

Email **contact@peachstatetechnologies.com** with the subject line "PeachPDF security". Include as much of the following as you can:

- The affected package and version (PeachPDF, PeachBrackets, PeachDrawing, PeachDrawing.PDF, and so on) and the .NET version and operating system you ran it on.
- A description of the problem and its impact.
- The smallest HTML, CSS, SVG, or PDF input, or code, that reproduces it.
- Whether the issue is already public or has a deadline for disclosure.

Reports are acknowledged by email. Please keep the details private until a fix is released, so users have a chance to update.

## What counts as a security issue

A vulnerability in PeachPDF or a library it depends on, such as memory exhaustion, an unbounded loop, unexpected file or network access, or the disclosure of data, triggered by crafted input. A crash or hang on valid input is handled under the same process.

## Supported versions

Which release series and which components receive security fixes is set out in the [support policy](docs/support.md#support-policy). In short, the current release series gets every kind of fix. A series in maintenance gets fixes for security vulnerabilities and crashes only, and only for the components listed in [Supported components in 0.9.x maintenance](docs/support.md#supported-components-in-09x-maintenance). A series in maintenance is also supported only on desktop and server operating systems that its supported .NET versions support; see [Supported operating systems in maintenance](docs/support.md#supported-operating-systems-in-maintenance). Series before 0.9 are not supported.

## Disclosure

When a fix is released, the release notes will describe the issue and credit the reporter, unless you ask to remain anonymous.
