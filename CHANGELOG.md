# Changelog

All notable changes to the `Foundation.*` packages are recorded here. Versions are set by `vX.Y.Z` git
tags (MinVer).

## Unreleased

- **New package `Foundation.Ccda`.** Parses an HL7 C-CDA (R2.1) document from a string or stream:
  header, patient, authors, custodian, sections (classified by template id, falling back to LOINC, with
  narrative as plain text) and problems, allergies, medications, immunizations, results, vital signs and
  procedures. Dependency-free, DTDs prohibited, input-capped, and parse errors carry only a reason and
  the XML line and position (documents are PHI).
- **Scope narrowed.** This repository now contains nine packages: `Foundation.Caching`,
  `Foundation.Ccda`, `Foundation.Compliance`, `Foundation.DataAccess`, `Foundation.Dto`, `Foundation.Exceptions`,
  `Foundation.Idempotency`, `Foundation.Persistence` and `Foundation.Tenancy`. The ASP.NET Core
  pipeline, time-zone, EventGrid, search-contract and gateway-contract packages were removed; each
  belongs with the single service or product that owns it.
- **Breaking — `Foundation.Caching`:** `CacheScope(TenantId, CommunityId)` is now
  `CacheScope(TenantId, SubScopeId)`. The sub-scope is whatever a service partitions a tenant by.
  The cache key segment changes from `:c{id}` to `:s{id}`, so existing cache entries are missed once
  after deploy.
- **Breaking — `Foundation.Compliance`:** the default `HipaaCheckOptions.SensitiveTypePattern` is now
  generic (`Person|People|Case|Note|Form|Individual|Contact`). Add domain terms by supplying your own
  pattern. New `HipaaCheckOptions.ExemptMemberNames` replaces the hard-coded `CommunityId`
  exemption; add your own scoping column names there.
- Documentation, comments and test fixtures were reworded to be domain-neutral.
