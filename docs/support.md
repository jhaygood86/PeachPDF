# Support

PeachPDF itself is free and open source under the [BSD 3-Clause license](license.md) — support plans are for help using it, never for the right to use it.

## Community support (free)

Free support for **bug reports, feature requests, and other issues** is available on GitHub — open an issue at [github.com/jhaygood86/PeachPDF/issues](https://github.com/jhaygood86/PeachPDF/issues) and it will be triaged like everything else in the project.

## Support policy

This policy applies to every release before 1.0. A policy for 1.0 and later will replace it when 1.0 ships. It covers PeachPDF and the libraries it depends on. Which components are covered once a series is in maintenance is set out for 0.9 in [Supported components in 0.9.x maintenance](#supported-components-in-09x-maintenance).

### Terms

- **Release series** — all releases that share a major and minor number. `0.9.x` is one series; `0.10.x` is the next.
- **LTS** and **STS** — .NET's long-term and standard-term support releases. Microsoft publishes each release's end-of-support date on its [.NET support policy page](https://dotnet.microsoft.com/platform/support/policy/dotnet-core).
- **Full support** — every kind of bug fix, new features, and compatibility work.
- **Maintenance** — fixes for security vulnerabilities and for crashes (unhandled exceptions, hangs, and out-of-memory failures on valid input) only.

### The rules

1. **Upgrade to 0.9 or later.** Series before 0.9 are not supported. Upgrade to 0.9 or newer to get help.
2. **Each series supports the .NET versions that were current when it launched.** A series is supported on every .NET release that was out at the time of its first release. For 0.9, that is .NET 8, 9, and 10. While a series has full support, it also adds support for newer .NET releases as they come out, and may support a .NET pre-release (such as a release candidate) on a best-effort basis. Once a series is in maintenance it **adds no new .NET versions**, and the first maintenance release removes support for any .NET pre-release it had. For 0.9, this means maintenance releases after Nov 6, 2026 do **not** support .NET 11; to use .NET 11, upgrade to 0.10.x.
3. **How long a .NET version stays supported.**
   - An **STS** release (such as .NET 9) is supported only until Microsoft ends support for it.
   - **LTS** releases (such as .NET 8 and .NET 10) are supported until the end of the **newest LTS the series launched with**. For 0.9, that is .NET 10, so 0.9 is supported on both .NET 8 and .NET 10 until .NET 10 reaches end of support.
4. **New series push older series into maintenance.** Once a new series has its first release, the previous series moves from full support to maintenance. Other bugs in a series in maintenance may still be fixed case by case, but there is no commitment. (Rule 6 describes the exception, a new series that replaces the old one.)
5. **A series ends** when the last .NET release it supports reaches its end date, as set out in rule 3.
6. **Some series replace the one before them.** PeachPDF and its related projects follow [semantic versioning](https://semver.org/) as closely as practical. Occasionally the major or minor number changes for an API break that is big enough to justify the bump but small for most users, and the new release supports the same .NET versions as the old one. In that case the maintainers may declare the new series a **continuation** of the old one. When they do, support for the old series **ends on the day its replacement is released**; there is no maintenance period, so upgrade to the new series. A version bump caused by **dropping support for older .NET versions is never a continuation**: the old series always keeps the support described in rules 3 and 4.

### Branches and outside contributions

- The `main` branch always tracks the **current** release series.
- When a series enters maintenance, a long-lived branch is created from its **last release tag** (for 0.9, the Nov 6, 2026 release), and maintenance releases are built from it.
- Anyone may send a pull request against an older series' branch. Such contributions are welcome and will be accepted, **including** improvements and features that fall outside this policy.
- **One condition:** a pull request to an older series that adds a feature or fixes a bug which also applies to the current series (`main`) must make the same change on `main`, too. Approval of the older-series pull request depends on it.
- This policy binds only Peach State Technologies, not outside contributors: what the maintainers commit to fix is what the rules above say, and nothing stops the community from doing more.

### Paid support extends the policy

- Paid customers can get a **specific fix** for any bug, of any type, in any supported series. A fix that already exists in a newer release can be **backported** to the older series you run.
- Paid support covers every series that is **currently supported** under the rules above. It does not bring back an unsupported series (including one replaced by a continuation series) or an unsupported .NET version.
- Requests are made by email under your plan; see [Paid support](#paid-support).

### Where each series stands today

| Series | Status | Details |
|---|---|---|
| 0.8 and older | Not supported | Upgrade to 0.9 or later. |
| 0.9.x | Full support until Nov 6, 2026, then maintenance | Last full-support release is **Nov 6, 2026**. Supported on .NET 8, 9, and 10 until **Nov 10, 2026**; on .NET 8 and 10 until **Nov 14, 2028**. |
| 0.10.x | First release **Nov 13, 2026** | Full support starts with the first release. Its own .NET list and end date will be added here when it ships. |

Key dates for 0.9:

| Date | What happens |
|---|---|
| Nov 6, 2026 | Last full-support release of 0.9. |
| Nov 10, 2026 | .NET 9 reaches end of support, so 0.9 is no longer supported on .NET 9. |
| Nov 13, 2026 | First release of 0.10.x. 0.9 is now in maintenance (security and crash fixes). Maintenance releases of 0.9 do not support .NET 11 (including its pre-release builds); use 0.10.x for .NET 11. |
| Nov 14, 2028 | .NET 10 reaches end of support. The 0.9 series, including its .NET 8 support, ends. |

If you are on 0.9 and want to keep receiving every kind of fix, move to 0.10 after Nov 13, 2026.

### Supported components in 0.9.x maintenance

While a series has full support, all of its libraries are fully supported, on their own or inside PeachPDF, provided you are on the current release series. The component limits below apply only once 0.9.x enters maintenance.

Once 0.9.x enters maintenance, the policy applies to **PeachPDF**. It also applies to every library PeachPDF depends on, **when that library is used inside PeachPDF**: PeachBrackets, PeachDrawing, PeachDrawing.PDF, PeachDrawing.Core, PeachDrawing.Text, and PeachImage.

| Component | Used inside PeachPDF | Used on its own |
|---|---|---|
| PeachPDF | Supported | Supported |
| PeachDrawing.PDF | Supported | **Supported** (a directly supported component) |
| PeachBrackets | Supported | Not covered by the 0.9.x maintenance policy |
| PeachDrawing, PeachDrawing.Core, PeachDrawing.Text | Supported | Not covered by the 0.9.x maintenance policy |
| PeachBrackets HtmlView libraries | Not applicable | Not covered by the 0.9.x maintenance policy |
| PeachImage | Supported | Covered only by the PeachImage project's own policies |

- **Full support for the rest arrives in 0.10.x.** PeachBrackets, the PeachDrawing libraries (other than PeachDrawing.PDF) and the PeachBrackets HtmlView libraries are expected to receive full support when used on their own in the 0.10.x series. That full support is contingent on staying on the current release series: once a newer series is released, the older one is in maintenance and these libraries are no longer covered when used on their own.
- **PeachImage is a separate project** with its own releases and support policy. Using it independently of PeachPDF is covered only by that project's policies.
- **Problems that reach PeachPDF are included.** A security or crashing issue found in any of these libraries that also affects PeachPDF is covered, and is fixed in the 0.9.x series.

### Supported operating systems in maintenance

While a series has full support, it is supported on every operating system the series targets. Once a series is in maintenance, support is limited to **desktop and server operating systems** that the supported .NET versions for that series support, as listed on Microsoft's [.NET support policy page](https://dotnet.microsoft.com/platform/support/policy/dotnet-core). An operating system that a supported .NET version no longer supports is no longer supported by the series either.

**For 0.9.x, mobile operating systems are not supported once it enters maintenance.** This includes Android and iOS, and the `PeachBrackets.Android` library, as well as other mobile platforms. To keep receiving fixes on a mobile operating system, use the 0.10.x series.

#### Operating systems supported for 0.9.x in maintenance

0.9.x in maintenance is supported on the desktop and server operating systems below. An operating system version must be supported by **both** a supported .NET version (.NET 8 or .NET 10, per Microsoft's published lists) **and** its own vendor. The table reflects both as of 2026-11-06.

| Operating system | Versions | Architectures | Vendor support ends |
|---|---|---|---|
| Windows 11 | 26H1 (Enterprise/Education), 25H2 (Enterprise/Education), 24H2 (Enterprise/Education, Enterprise LTSC, IoT Enterprise LTSC) | Arm64, x64, x86 | 26H1: 2029-03-13; 25H2: 2028-10-10; 24H2: 2027-10-12 (LTSC 2029-10-09; IoT LTSC 2034-10-10) |
| Windows 11 (Home and Pro) | 26H1, 25H2 | Arm64, x64, x86 | 26H1: 2028-03-14; 25H2: 2027-10-12 |
| Windows 10 | 21H2 (Enterprise LTSC, IoT Enterprise LTSC), 1809 (Enterprise LTSC) | x64, x86 | 21H2: 2027-01-12 (IoT LTSC 2032-01-13); 1809: 2029-01-09 |
| Windows Server | 2025, 2022, 2019, 2016 | x64 | 2025: 2034-11-14; 2022: 2031-10-14; 2019: 2029-01-09; 2016: 2027-01-12 |
| Windows Server Core | 2025, 2022, 2019, 2016 | x64 | As Windows Server |
| Nano Server | 2025, 2022, 2019 | x64 | As Windows Server |
| macOS | 27, 26, 15 | Arm64, x64 | While Apple ships security updates for it |
| Alpine Linux | 3.24, 3.23, 3.22 | Arm32, Arm64, x64 | 3.24: 2028-06-01; 3.23: 2027-11-01; 3.22: 2027-05-01 |
| Azure Linux | 3.0 | Arm64, x64 | While Microsoft supports it |
| CentOS Stream | 10, 9 | Arm64, ppc64le, s390x, x64 | 10: 2030-05-31; 9: 2027-05-31 |
| Debian | 13; 12 (Debian LTS, while .NET 8 is supported) | Arm32, Arm64, x64 | 13: 2030-06-30; 12: 2028-06-30 |
| Fedora | 44, 43 | Arm32, Arm64, x64 | 44: 2027-06-02; 43: 2026-12-01 |
| openSUSE Leap | 16.0 | Arm64, x64 | 2027-10-31 |
| Red Hat Enterprise Linux (and compatible derivatives) | 10, 9, 8 | Arm64, ppc64le, s390x, x64 | 10: 2035-05-31; 9: 2032-05-31; 8: 2029-05-31 |
| SUSE Linux Enterprise | 16.0, 15.7 | Arm64, x64 | 16.0: 2027-11-30; 15.7: 2031-07-31 |
| Ubuntu | 26.04, 24.04, 22.04 | Arm32, Arm64, x64 | 26.04: 2031-05-29; 24.04: 2029-05-31; 22.04: 2027-06-01 |

- **Support ends with the vendor.** A version stops being supported for 0.9.x on the earlier of the vendor's end-of-support date above and the date its .NET version support ends, even if the table has not been updated yet. Dates are the vendor's standard or general support end, as published by [endoflife.date](https://endoflife.date); Debian 12 is covered by the Debian LTS team.
- **Paid extended programs do not count.** Extended Security Updates (Windows), Extended Life Cycle Support (Red Hat), Long Term Service Pack Support (SUSE) and Ubuntu Pro/ESM extend a vendor's support only for paying customers, so they do not extend support for 0.9.x. This is why Windows 11 23H2, Windows 10 1607, Windows Server 2012 and 2012 R2, and Alpine 3.21, which .NET still lists, are not in the table.
- **The list follows Microsoft's and each vendor's.** When Microsoft adds a version of one of these operating systems for .NET 8 or .NET 10, it becomes supported for 0.9.x once its vendor supports it. Check Microsoft's [.NET 8](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md) and [.NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) lists for the current state.
- **Operating systems not listed are not supported**, including Android, iOS, iPadOS, tvOS and other mobile or embedded platforms.

## Paid support

Paid support is available for **$25/month per developer** or **$250/year per developer**:

- **[Sign up monthly](https://customer.peachstatetechnologies.com/Signup/ret8myBIEZs3WBlMHJqGzxgUAaJ60QSvoifndYG0w6A?term=monthly)** — $25/month per developer.
- **[Sign up annually](https://customer.peachstatetechnologies.com/Signup/ret8myBIEZs3WBlMHJqGzxgUAaJ60QSvoifndYG0w6A?term=annually)** — $250/year per developer.

A PeachPDF paid support plan also covers PeachDrawing, PeachImage, and PeachBrackets (every package that ships with PeachPDF).

It includes:

- **Direct email support** — a private channel straight to the maintainer, no public issue tracker required.
- **Priority feature requests and bug fixes** — your reports move to the front of the queue.
- **Fixes for older series** — specific fixes backported to any currently supported series, as described in the [support policy](#support-policy).
- **Email-based consulting** on how best to use and integrate PeachPDF into your application and workflow.

For an additional cost, **custom development** to integrate PeachPDF into your product is also available.

Paid support is provided by **Peach State Technologies**. Email [justin@peachstatetechnologies.com](mailto:justin@peachstatetechnologies.com) for more details.

## Sponsorship as an alternative

Prefer to support the project without a formal contract? [Sponsoring PeachPDF on GitHub](sponsorship.md) is an alternative to a formal support plan — sponsors who donate can get paid support under the same terms as customers who purchase one, with every $25 donated counting as one month of support for the sponsor. See [Sponsorship](sponsorship.md) for the details.
