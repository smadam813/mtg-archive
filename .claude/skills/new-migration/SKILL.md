---
name: new-migration
description: Generate an EF Core migration for the MtgArchive schema with dotnet-ef. Use whenever an entity, index, constraint, or derived table changes; the migration files are generated, never written by hand.
argument-hint: "<PascalCaseName>"
---

The model is the source of truth. Change the entities in `src/MtgArchive/Data/` and their configuration in `ArchiveDbContext`, then generate from the repo root:

```sh
dotnet tool restore
dotnet ef migrations add $ARGUMENTS --project src/MtgArchive
```

Generation needs no database connection. Name the migration for the schema change (`SourceProfiles`, `TutorDelayTurns`); dotnet-ef adds the timestamp prefix.

Then:

1. Read the generated `Up` method. Confirm that it contains only the change you intended. If it does not, fix the model, run `dotnet ef migrations remove --project src/MtgArchive`, and generate again. Remove only the migration you just added; a migration on `main` is history.
2. Do not edit a migration, its `.Designer.cs`, or the model snapshot. A PreToolUse hook denies Write and Edit on the `.Designer.cs` files and the snapshot. On a migration's own file it asks the user, for the step 4 exception only. Do not go around it with a shell command.
3. A derived table (rebuilt by an ingest phase) has a permanent `<table>_staging` twin. Declare the twin in the model, not in the migration: a staging entity class (see `LegalityStaging`) with the same configuration. Name every key, foreign key, and index explicitly; each staging name is the live name plus `_staging`. Add the table and its names to the phase's `SwappedTables` array. `Ingest/DerivedStateSwap.cs` swaps the pair by rename and depends on this layout.
4. If the change needs DDL that the model cannot express (a SQL function, an extension), stop and ask the user before you continue. That DDL goes in a `migrationBuilder.Sql` call, which is a hand edit. `LegalCandidatesFunction` is the precedent. The raw-SQL limit in `.claude/rules/service.md` applies to query code, not to DDL.
5. Run `/verify`. The Postgres-backed tests migrate from an empty database, so a broken migration fails there.

Done when the migration applies in the test suite and the model snapshot matches the entities.
