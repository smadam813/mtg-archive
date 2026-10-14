---
name: new-endpoint
description: Add an HTTP endpoint to the MtgArchive service, or change an existing route's contract, by the repo's wire conventions, legality boundary, and OpenAPI test. Use when an issue asks for a new route, a new request or response field, a new error, or a changed path or verb.
---

`docs/api-conventions.md` is the wire contract. Read it first. Then:

1. Pick the slice that the endpoint serves (`Cards`, `Rules`, `Banlist`, `Bracket`, `CollectionImport`, `Ingest`, `Embedding`, `Simulation`). Map the route in that slice's `IEndpointMapper`. Register a new mapper as a singleton in `Program.cs`. A computed query is `POST`; a reference fetch is `GET`. Put a wire type in `Api/` only when more than one slice uses it.
2. If the endpoint returns cards as candidates, it is behind the legality boundary (ADR 0001). Derive the request from `Api/CandidateQueryRequest.cs` and call `CandidateQuery.ResolveAsync` first. Compose your predicates, the KNN, and `ArchiveDbContext.AvailableCopies` onto the query it returns, so one SQL statement runs. Do not filter rows in C#. `tests/MtgArchive.Tests/SeamProbe.cs` (`POST /_seam/candidates`) is the worked example. `IndexFuzzHarness` finds the route by its base type, so the property suites cover it with no registration.
3. If the fuzzed `role` or `color_identity` contradicts the endpoint's contract, stop and ask the user before you leave the base type. `POST /commander-alternatives` is the one approved exception. It still calls `CandidateQuery.ResolveAsync`, and `CommanderAlternativesTests` covers its boundary properties directly.
4. Resolve each optional version parameter through `Api/VersionParameter.cs` and echo it in the `versions` block. An unknown pin is a 4xx problem+json, never a silent fallback. Add each new error as a factory in `Api/Problems.cs`. Write extension members through `Problems.WithExtension`; the framework writes extension keys verbatim, outside the snake_case policy. Every citation is the one `Citation` shape, and a reference endpoint serves its id.
5. Assert the route and method in `tests/MtgArchive.Tests/OpenApiTests.cs`. Write the endpoint's tests through `ArchiveApp` in a flat `<Subject>Tests.cs` file.
6. If you change or remove an existing route, find the plugin skills that send it: `grep -rn "<route>" plugin/mtg-archive`. Update the skill text, its references, and its `*SkillTests.cs` pins in the same PR.
7. Run `/verify`.

Done when the route is in `/openapi.json`, its tests and the OpenAPI assertion pass, every wire field and extension member is snake_case, and every plugin skill that sends the route matches its contract.
