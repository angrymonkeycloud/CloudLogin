# Migrating a CloudLogin deployment to the core storage model

`CloudLoginMigrationService` (`CloudLogin.Server/Core/Migration/`) moves a legacy deployment — the mixed single Cosmos container plus per-user blob credential documents — into the seven-container core described in [docs/architecture-core.md](architecture-core.md).

## Guarantees

- **Idempotent.** User documents upsert; identity keys are create-only, and a replay of the same user's claim is a no-op. Running the migration twice produces the same result as running it once.
- **Checkpointed.** Completed user ids are recorded (blob `migration/checkpoint.json`) every `BatchSize` users. A crashed run restarts where it stopped; a replayed batch is harmless because of idempotence.
- **Dry run first.** `DryRun = true` (the default) reads and validates everything, produces the full report, and writes nothing.
- **Preserves** user ids, password hashes (verbatim — existing passwords keep working), profiles, timestamps, and provider links whose issuer is known safely.
- **Preserves contact ids across re-runs.** A contact's id is assigned once and reused from the already-migrated document on every subsequent run, so a restart cannot mint fresh ids and orphan the credentials and identity rows pointing at the old ones.
- **Keeps identity values out of the report.** A canonical value is an identity-hash input, so review items name the identity type and the user ids involved rather than the address itself.
- **Never picks a winner.** A duplicate identity (the same email on two users) or a malformed document becomes a `Review` issue in the report, with both user ids named. Nothing is merged, dropped, or resolved by taking a first match.
- **Converts provider identifiers to `(issuer, subject)` only for safely known issuers** (Google, Microsoft, Facebook, Twitter — `KnownProviderIssuers`). Anything else is flagged `provider-identifier-unconverted` for review.
- **Leaves the legacy container untouched.** `LegacyCosmosUserSource` is strictly read-only; the migration never writes, alters, or deletes legacy data, and nothing ever deletes the legacy container automatically.

## Before you start: the identity secret

The index the migration writes is keyed (see [The key](architecture-core.md#the-key)). Settle the key **before the dry run** and use the same one for every subsequent run and for the deployment that goes live: a migration run under one key and a server started under another produce two disjoint indexes, so every migrated account resolves to nothing and a returning user is treated as a new person.

The migration host and the server must be configured with the *same* `CloudLogin:IdentityHmacSecret`. Nothing derives or generates one, so a mismatch is not subtle: the migration writes an index the server cannot read at all.

For the same reason there is no in-place upgrade for an index written by an earlier build that hashed with bare SHA-256. Those row keys cannot be recomputed without the plaintext, which was never intended to survive. A deployment holding such an index re-runs the migration against a fresh table; a disposable environment can simply drop it.

## Procedure

1. **Provision** — configure `CloudLoginWebConfiguration.Core` (and `AzureStorage`) on a staging instance; `CosmosCoreDatabase.ProvisionAllAsync` creates the seven containers with their TTL settings, and the table stores create themselves on first use.

2. **Dry run** — run with `DryRun = true`. Review the saved report (`migration/report-*-dryrun.json`): counts, duplicate identities, malformed users, unconverted provider identifiers. Resolve review items at the source where possible.

3. **Migrate** — run with `DryRun = false`. Progress checkpoints continuously; on failure, run again and it resumes.

4. **Catch-up** — after the bulk run, either impose a short write freeze on the legacy deployment or simply re-run the migration once more immediately before cutover: idempotent processing makes the re-run a cheap catch-up pass over anything that changed mid-run. (For very large stores, a Cosmos change-feed listener over the legacy container feeding the same per-user routine is the equivalent continuous form.)

5. **Consistency report** — every run ends with a consistency check (`LegacyUserCount` versus `CoreUserCount`, with malformed users accounted); the report is saved to blob storage. Do not cut over while `CountsConsistent` is false or unresolved `Review`/`Error` issues remain.

6. **Cutover** — set `Core` on the production configuration and restart. Every authority and every API version now uses only the new core. There are no ongoing legacy/new dual writes — cutover is a switch, not a bridge.

7. **Rollback window** — keep the legacy container read-only for the deployment's chosen rollback period. Rolling back is unsetting `Core`; because the migration never mutated legacy data, the old model is exactly as it was (changes made after cutover stay in the core and would need re-migration forward).

8. **Retirement** — after the rollback window, retire the legacy container manually. This is a deliberate human action; no code path deletes it.

## Invocation

The service is a plain application service, so a host can expose it however its operations require (an admin console command, a one-off hosted job):

```csharp
CloudLoginMigrationService migration = new(
    new LegacyCosmosUserSource(legacyContainer),
    new BlobMigrationCheckpointStore(storageConfiguration),
    userRepository, credentialRepository, identityKeyStore,
    normalization, coreConfiguration,
    blobSecurity: securityStore,          // migrates passkeys and the authenticator app
    dataProtection: dataProtectionProvider); // wraps TOTP secrets on the way in

MigrationReport report = await migration.RunAsync(new MigrationOptions
{
    DryRun = false,
    BatchSize = 100,
    ResumeFromCheckpoint = true
});
```

## Report categories

| Category | Severity | Meaning |
| --- | --- | --- |
| `malformed-user` | Error | No id, or no inputs; skipped, needs manual handling |
| `malformed-identity` | Review | An input could not be normalized |
| `duplicate-identity` | Review | The identity is claimed by another user; both ids named |
| `user-partially-claimable` | Review | The user migrated but one or more identities belong to someone else |
| `provider-identifier-unconverted` | Review | Provider identifier present but no safely known issuer |
| `totp-unprotected` | Error | Authenticator secret found but no Data Protection provider supplied |
| `blob-credentials-unreadable` | Review | The per-user blob security document could not be read |

## Related pages

- [docs/architecture-core.md](architecture-core.md)
- [docs/database-schema.md](database-schema.md) — the legacy source schema
