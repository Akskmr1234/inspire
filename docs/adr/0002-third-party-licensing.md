# ADR 0002 — Third-party licensing and the AutoMapper removal

- **Status:** Accepted
- **Date:** 2026-08-05
- **Deciders:** Engineering
- **Supersedes:** the library list in the engagement brief, for the three packages named below

## Context

The engagement brief specified MediatR, AutoMapper, and (implicitly, via “unit tests”) FluentAssertions. Between 2024 and 2026 all three relicensed from permissive open-source terms to paid commercial terms:

| Package          | Last permissive release | Licence from | Now requires                        |
| ---------------- | ----------------------- | ------------ | ----------------------------------- |
| MediatR          | 12.5.0 (Apache-2.0)     | 13.0.0       | paid licence (Lucky Penny Software) |
| AutoMapper       | 13.0.1 (MIT)            | 14.0.0       | paid licence (Lucky Penny Software) |
| FluentAssertions | 7.2.2 (Apache-2.0)      | 8.0.0        | paid licence (Xceed)                |

The obvious response — pin the last permissive release of each — works for two of the three. It does **not** work for AutoMapper.

`dotnet restore` raised `NU1903` against AutoMapper 13.0.1. Checking the advisory:

```
GHSA-rvv3-g6hj-g44x — AutoMapper Vulnerable to Denial of Service via Uncontrolled Recursion
Severity: HIGH
  vulnerable: < 15.1.1              patched: 15.1.1
  vulnerable: >= 16.0.0, < 16.1.1   patched: 16.1.1
```

The last MIT release (13.0.1) sits inside the vulnerable range. Because the library is now commercial, **13.0.1 will never receive a backported patch.** There is therefore no version of AutoMapper that is simultaneously free to use and free of a known high-severity advisory.

## Decision

**1. AutoMapper is removed. DTO mapping is hand-written.**

Mapping lives in `ERP.Application` as explicit extension methods (`ToDto()`, `ToDomain()`), organised beside the feature that owns them.

**2. MediatR is pinned to 12.5.0**, the last Apache-2.0 release, which has no known vulnerabilities. Handlers and requests are additionally expressed through our own interfaces in `ERP.Application.Abstractions.Messaging`, so MediatR appears in the composition root and the pipeline behaviours but not throughout feature code.

**3. FluentAssertions is not used.** Tests assert with **Shouldly** (BSD-3-Clause).

**4. `Newtonsoft.Json` is pinned transitively to 13.0.4.** Hangfire and Swashbuckle pull in 11.0.1, which carries `GHSA-5crp-9r3c-p9vr` (HIGH). `CentralPackageTransitivePinningEnabled` plus a `PackageVersion` entry lifts every transitive reference to a patched release.

## Consequences

**Good**

- No known-vulnerable dependency ships in the product.
- No per-developer licence cost is imposed on the customer without their decision.
- Hand-written mapping is compile-time checked. A renamed or removed domain property becomes a build error rather than a silently-null field at runtime — a materially better failure mode for financial documents.
- No startup reflection scan and no per-map allocation overhead. This matters against the “1,000+ concurrent users, sub-3-second response” requirement.
- Mapping code is directly debuggable; you can set a breakpoint in it.

**Bad**

- Mapping is more verbose. A 30-property entity needs 30 assignments. Mitigated by keeping DTOs narrow — most queries project directly to a DTO in the EF expression tree, which is faster than mapping a materialised entity anyway.
- Deviates from the brief's stated stack. Recorded here so the deviation is a documented decision rather than an accident.

**Pinned-version risk**

MediatR 12.5.0 will not receive future security patches. Accepted because it is a small in-process dispatcher with no network, parsing, or deserialisation surface — the realistic vulnerability classes for such a library are close to nil. Should that change, the abstraction in `Abstractions/Messaging` confines the replacement to the composition root and the behaviour pipeline.

## Alternatives considered

| Alternative                                       | Why rejected                                                                                                                                                                                                |
| ------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Buy AutoMapper 16.1.1+ commercial licences        | Not our decision to commit the customer to recurring per-seat cost. Available to them later if preferred; the mapping seam makes adoption straightforward.                                                  |
| Ship AutoMapper 13.0.1 with the advisory accepted | Unacceptable in a financial system, and it would fail any dependency-scanning gate in CI or a customer security review.                                                                                     |
| Fork AutoMapper 13.0.1 and patch it               | Perpetual maintenance burden and licence ambiguity around the fork point.                                                                                                                                   |
| Mapperly (source generator, MIT)                  | Genuinely good option — compile-time generated, no licence issue. Rejected only to avoid adding a dependency for something a plain extension method does clearly. Worth revisiting if mapping volume grows. |

## Action required from the business

Confirm you are content with hand-written mapping, or instruct us to purchase AutoMapper licences. No further work depends on the answer — the seam is in place either way.
