# Files and backups in Cloudflare R2

Two optional features keep the ERP's files in Cloudflare R2 (or any S3-compatible
storage) instead of in PostgreSQL. Each is off until its variables are set; without
them everything works exactly as before.

| What                                                                                                                 | Where it runs          | Turned on by          |
| -------------------------------------------------------------------------------------------------------------------- | ---------------------- | --------------------- |
| **Uploaded files**: product, customer/supplier/employee and return photographs, and the logos in the print templates | the API                | `Erp__Storage__S3__*` |
| **Database backups**: scheduled `pg_dump` of the ERP's databases, kept for 30 days                                   | one more small service | `BACKUP_S3_*`         |

---

## Setting up R2

In Cloudflare:

1. **R2 → Create bucket**, e.g. `erp-files`. Keep it private (the default).
2. **R2 → Manage API tokens → Create API token**, with _Object Read & Write_ on that
   bucket. It shows an **Access Key ID** (32 characters), a **Secret Access Key**
   (64 characters) and the endpoint `https://<account-id>.r2.cloudflarestorage.com`.
   Use those two keys, not the token value shown above them.

Files and backups can share a bucket: files go under `files/`, backups under
`backups/`. A **separate bucket and token for backups** is safer: a backup then
cannot be touched with the key the API holds.

No CORS rule is needed: the browser only shows pictures from the bucket, it never
writes to it.

---

## Uploaded files

### API variables

| Variable                              | Default | Notes                                                                                                                                                           |
| ------------------------------------- | ------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Erp__Storage__S3__Enabled`           | `false` | `true` turns it on.                                                                                                                                             |
| `Erp__Storage__S3__Endpoint`          | —       | `https://<account-id>.r2.cloudflarestorage.com` (an EU bucket: `https://<account-id>.eu.r2.cloudflarestorage.com`). A bucket name pasted on the end is ignored. |
| `Erp__Storage__S3__Bucket`            | —       | e.g. `erp-files`.                                                                                                                                               |
| `Erp__Storage__S3__AccessKeyId`       | —       | From the R2 API token.                                                                                                                                          |
| `Erp__Storage__S3__SecretAccessKey`   | —       | From the R2 API token. Keep it secret.                                                                                                                          |
| `Erp__Storage__S3__Region`            | `auto`  | What R2 expects. Another S3 service may need its region.                                                                                                        |
| `Erp__Storage__S3__KeyPrefix`         | `files` | The folder in the bucket.                                                                                                                                       |
| `Erp__Storage__S3__LinkMinutes`       | `720`   | How long a link to a picture works (12 hours). The screens fetch fresh links as they go.                                                                        |
| `Erp__Storage__S3__PublicBaseUrl`     | —       | Only if the bucket is made public (a custom domain or its `r2.dev` address): links then point there and never expire. Leave empty to keep the bucket private.   |
| `Erp__Storage__S3__MoveExistingFiles` | `true`  | Moves the files already in the database to the bucket, in the background.                                                                                       |

All four of `Endpoint`, `Bucket`, `AccessKeyId` and `SecretAccessKey` are needed;
with any missing, files stay in the database.

### What changes

- **A new upload** goes to the bucket, at
  `files/<tenant>/<firm>/<products|parties|rmas|settings>/…`. The database keeps only
  a short reference to it (`s3:files/…`).
- **Showing a picture**: the API answers with a signed link to the private bucket
  instead of the picture itself. Nothing changes on the screens.
- **Print logos** go to the bucket too; the print setup still comes back to the
  screen with the logo in it, exactly as it was saved.
- **Replacing or removing** a picture deletes the old file. **Clearing a firm's data**
  (Platform → Data reset) deletes the pictures of what it clears.
- **Files already in the database** move across in the background about 15 seconds
  after the API starts, a few at a time. The log says
  `Moved N records' files from the database to file storage`. Anything it cannot
  move stays in the database, still shown as before, and is tried again on the next
  start. Nothing is held up while it runs.

> **Keep it on once it is on.** Turned off again, pictures already in the bucket are
> not shown (they are not copied back). Turn it back on and they return.

If the keys are wrong, a save that includes a picture says _"The file could not be
stored just now"_ and saves nothing, and the API log names the cause and the fix
(`SignatureDoesNotMatch: … use the S3 'Secret Access Key' of the same R2 API token`).

---

## Database backups

A small service, built from [`infrastructure/backup`](../infrastructure/backup),
that takes a `pg_dump` of the ERP's database (and of the audit trail's database,
when it has one of its own) every 24 hours and uploads it to R2:

```
backups/main/main-20261123T053000Z.dump
backups/audit/audit-20261123T053000Z.dump
```

- Each dump is checked to read back before it is uploaded.
- Backups older than 30 days are deleted, but the newest 7 are always kept, so a long
  outage never empties the bucket.
- It carries on from the newest backup in the bucket, so a restart or a redeploy
  neither backs up again at once nor skips one that is due.
- A failed backup is logged with its cause and tried again in 15 minutes.
- It needs no volume.

### Deploying it on OneDeploy

Add a third service:

| Root directory          | Dockerfile   | Container port | Health check path |
| ----------------------- | ------------ | -------------- | ----------------- |
| `infrastructure/backup` | `Dockerfile` | 8080           | `/`               |

Run **one instance**. Give it network access to the same PostgreSQL as the API.

### Its variables

| Variable                      | Required | Notes                                                                                                                  |
| ----------------------------- | -------- | ---------------------------------------------------------------------------------------------------------------------- |
| `ConnectionStrings__Postgres` | yes      | **The same value the API has.** (Or `BACKUP_DATABASE_URL` as `postgres://user:password@host:5432/database`.)           |
| `ConnectionStrings__Audit`    | no       | The same value the API has, if the audit trail has its own database. Without one, the trail is inside the main backup. |
| `BACKUP_S3_ENDPOINT`          | yes      | As for files. Falls back to `Erp__Storage__S3__Endpoint`.                                                              |
| `BACKUP_S3_BUCKET`            | yes      | Falls back to `Erp__Storage__S3__Bucket`.                                                                              |
| `BACKUP_S3_ACCESS_KEY_ID`     | yes      | Falls back to `Erp__Storage__S3__AccessKeyId`.                                                                         |
| `BACKUP_S3_SECRET_ACCESS_KEY` | yes      | Falls back to `Erp__Storage__S3__SecretAccessKey`.                                                                     |
| `BACKUP_S3_REGION`            | no       | Default `auto`.                                                                                                        |
| `BACKUP_S3_PREFIX`            | no       | Default `backups`.                                                                                                     |
| `BACKUP_EVERY_HOURS`          | no       | Default `24`.                                                                                                          |
| `BACKUP_KEEP_DAYS`            | no       | Default `30`.                                                                                                          |
| `BACKUP_KEEP_MIN`             | no       | Default `7`: always kept, however old.                                                                                 |
| `BACKUP_RETRY_MINUTES`        | no       | Default `15`.                                                                                                          |

If the R2 keys or bucket are wrong, the service **stops at start** with the cause and
the fix in its log, instead of reporting healthy and failing every night.

### Checking on it

- `GET /` always answers `200` while it runs: that is the platform's health check.
- `GET /status` answers `503` when the last backup failed, or none has succeeded for
  two days. Point an uptime monitor at it. Both show the last backup, the last error
  and when the next one is due.
- To list the backups, run in the service's console:

  ```
  python3 /opt/backup/backup.py list
  ```

- To take one now: `python3 /opt/backup/backup.py now`.

### Restoring

A restore **replaces everything** in the database with the backup, in one
transaction: it either completes or changes nothing.

1. **Stop the API** (scale it to 0), so nothing writes while the restore runs.
2. In the backup service's console:

   ```
   python3 /opt/backup/backup.py restore main latest --yes
   ```

   `latest` can be a key from `list` instead. Add `--target postgres://…` to restore
   into another, empty database instead, to look at an old backup without touching
   the live one. Restore `audit` the same way, if it has its own database.

3. **Start the API** again.

From your own computer, with Docker, the same image does it:

```
docker build -t erp-backup infrastructure/backup
docker run --rm -e BACKUP_S3_ENDPOINT=… -e BACKUP_S3_BUCKET=… \
  -e BACKUP_S3_ACCESS_KEY_ID=… -e BACKUP_S3_SECRET_ACCESS_KEY=… \
  erp-backup restore main latest --yes --target postgres://user:password@host:5432/database
```

The image's `pg_dump` and `pg_restore` are PostgreSQL 18's: they back up and restore
every server version from 9.2 on, PostgreSQL 16 included.

### Locally

```
docker compose -f docker-compose.yml -f docker-compose.backup.yml up -d backup
```

with the `BACKUP_S3_*` values in a `.env` file next to the compose files. Its status
page is on `http://127.0.0.1:5200/status`.

---

## Testing against a real bucket

The API's storage has an integration test that runs only when a bucket is named:

```
ERP_TEST_S3_ENDPOINT=https://<account-id>.r2.cloudflarestorage.com \
ERP_TEST_S3_BUCKET=erp-test ERP_TEST_S3_ACCESS_KEY_ID=… ERP_TEST_S3_SECRET_ACCESS_KEY=… \
dotnet test backend/tests/ERP.Infrastructure.Tests --filter "FullyQualifiedName~S3FileStoreTests"
```

It stores a picture, fetches it through its signed link with no credentials, reads it
back and deletes it.
