"""Backs the ERP's PostgreSQL databases up to S3-compatible storage (Cloudflare R2).

    backup.py run                  back up on a schedule, with a health page on :8080
    backup.py now                  back up once, now, and stop
    backup.py list [main|audit]    list the backups in the bucket, newest first
    backup.py restore main|audit KEY|latest --yes [--target URL]
                                   restore a backup over a database (stop the API first)

Each backup is a pg_dump custom-format archive, checked to be readable before it is
uploaded to <prefix>/<main|audit>/<main|audit>-<YYYYMMDDTHHMMSSZ>.dump. Backups older than
BACKUP_KEEP_DAYS are deleted, but the newest BACKUP_KEEP_MIN are always kept, so a long
outage never leaves the bucket empty.

Settings (environment). The database settings take the same values the API is given:
    ConnectionStrings__Postgres    the ERP's database (or BACKUP_DATABASE_URL, postgres://...)
    ConnectionStrings__Audit       the audit trail's database, when it has one of its own
                                   (or BACKUP_AUDIT_DATABASE_URL)
    BACKUP_S3_ENDPOINT             https://<account-id>.r2.cloudflarestorage.com
    BACKUP_S3_BUCKET               the bucket
    BACKUP_S3_ACCESS_KEY_ID        an R2 API token's S3 access key id
    BACKUP_S3_SECRET_ACCESS_KEY    and its secret
                                   (each BACKUP_S3_* falls back to the API's Erp__Storage__S3__*)
    BACKUP_S3_REGION               default auto (what R2 expects)
    BACKUP_S3_PREFIX               default backups
    BACKUP_EVERY_HOURS             default 24
    BACKUP_KEEP_DAYS               default 30
    BACKUP_KEEP_MIN                default 7
    BACKUP_RETRY_MINUTES           after a failed backup, default 15
    PORT                           the health page, default 8080
"""

import datetime
import json
import os
import re
import signal
import subprocess
import sys
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit

import boto3
from boto3.s3.transfer import TransferConfig
from botocore.config import Config
from botocore.exceptions import BotoCoreError, ClientError

NAME = re.compile(r"^(main|audit)-(\d{8}T\d{6}Z)\.dump$")
STAMP = "%Y%m%dT%H%M%SZ"


def log(message: str) -> None:
    print(f"[backup] {message}", file=sys.stderr, flush=True)


def setting(*names: str, default: str = "") -> str:
    """The first of the names that is set, without the spaces, line breaks or quotes a paste brings."""
    for name in names:
        value = os.environ.get(name, "").strip().strip("'\"").strip()
        if value:
            return value
    return default


def number(name: str, default: float) -> float:
    try:
        return float(setting(name, default=str(default)))
    except ValueError:
        sys.exit(f"[backup] {name} should be a number.")


PREFIX = setting("BACKUP_S3_PREFIX", default="backups").strip("/")
EVERY = max(0.25, number("BACKUP_EVERY_HOURS", 24)) * 3600
KEEP_DAYS = max(1.0, number("BACKUP_KEEP_DAYS", 30))
KEEP_MIN = max(1, int(number("BACKUP_KEEP_MIN", 7)))
RETRY = max(1.0, number("BACKUP_RETRY_MINUTES", 15)) * 60
BUCKET = setting("BACKUP_S3_BUCKET", "Erp__Storage__S3__Bucket")


# --- Where the databases are ---------------------------------------------------------

SSL_MODES = {"disable": "disable", "allow": "allow", "prefer": "prefer", "require": "require",
             "verifyca": "verify-ca", "verify-ca": "verify-ca", "verifyfull": "verify-full",
             "verify-full": "verify-full"}
NPGSQL_KEYS = {
    "host": "PGHOST", "server": "PGHOST",
    "port": "PGPORT",
    "database": "PGDATABASE", "db": "PGDATABASE",
    "username": "PGUSER", "user": "PGUSER", "userid": "PGUSER", "user id": "PGUSER", "uid": "PGUSER",
    "password": "PGPASSWORD", "pwd": "PGPASSWORD",
    "sslmode": "PGSSLMODE", "ssl mode": "PGSSLMODE",
}


def connection(label: str) -> dict | None:
    """The libpq environment for one database: from a postgres:// URL, or from the
    Npgsql connection string the API uses (Host=...;Database=...;Username=...)."""
    if label == "main":
        url = setting("BACKUP_DATABASE_URL", "DATABASE_URL")
        npgsql = setting("ConnectionStrings__Postgres")
    else:
        url = setting("BACKUP_AUDIT_DATABASE_URL")
        npgsql = setting("ConnectionStrings__Audit")

    if url:
        return {"PGDATABASE": url} if url.startswith(("postgres://", "postgresql://")) else None
    if not npgsql:
        return None

    env = {}
    for part in npgsql.split(";"):
        if "=" not in part:
            continue
        key, value = part.split("=", 1)
        target = NPGSQL_KEYS.get(re.sub(r"\s+", " ", key.strip().lower()))
        if target == "PGSSLMODE":
            value = SSL_MODES.get(value.strip().lower().replace(" ", ""), "prefer")
        if target:
            env[target] = value.strip()
    return env


def databases() -> dict:
    found = {label: env for label in ("main", "audit") if (env := connection(label))}
    if "main" not in found:
        sys.exit("[backup] Set ConnectionStrings__Postgres (the same value the API has) or "
                 "BACKUP_DATABASE_URL (postgres://user:password@host:5432/database).")
    return found


def run_pg(command: list, env: dict, **kwargs) -> subprocess.CompletedProcess:
    """Runs a PostgreSQL tool with the connection in its environment, never on its
    command line, where the password would show in a process list. A postgres:// URL
    goes in as the database name, which libpq reads as a whole connection string."""
    full = dict(os.environ, **{k: v for k, v in env.items() if k != "PGDATABASE"})
    if "PGDATABASE" in env:
        command = [*command, f"--dbname={env['PGDATABASE']}"]
    return subprocess.run(command, env=full, capture_output=True, text=True, **kwargs)


# --- The bucket ----------------------------------------------------------------------

def endpoint() -> str:
    raw = setting("BACKUP_S3_ENDPOINT", "Erp__Storage__S3__Endpoint")
    parts = urlsplit(raw)
    if not parts.scheme or not parts.netloc:
        sys.exit("[backup] Set BACKUP_S3_ENDPOINT, e.g. https://<account-id>.r2.cloudflarestorage.com")
    return f"{parts.scheme}://{parts.netloc}"


def client():
    options = {"retries": {"max_attempts": 5, "mode": "standard"}, "s3": {"addressing_style": "path"}}
    try:
        # Newer SDKs send checksums R2 does not take on every request; ask for them only
        # where S3 requires them.
        config = Config(request_checksum_calculation="when_required",
                        response_checksum_validation="when_required", **options)
    except TypeError:
        config = Config(**options)
    return boto3.client(
        "s3",
        endpoint_url=endpoint(),
        aws_access_key_id=setting("BACKUP_S3_ACCESS_KEY_ID", "Erp__Storage__S3__AccessKeyId"),
        aws_secret_access_key=setting("BACKUP_S3_SECRET_ACCESS_KEY", "Erp__Storage__S3__SecretAccessKey"),
        region_name=setting("BACKUP_S3_REGION", "Erp__Storage__S3__Region", default="auto"),
        config=config,
    )


HINTS = {
    "InvalidAccessKeyId": "The access key id is not a known key. Use the S3 'Access Key ID' shown "
                          "when the R2 API token was created, not the token value.",
    "SignatureDoesNotMatch": "The secret access key does not match the key id. Use the S3 'Secret "
                             "Access Key' of the same R2 API token.",
    "Unauthorized": "The credentials were rejected. Use the S3 'Access Key ID' and 'Secret Access "
                    "Key' of an R2 API token, not the token value.",
    "AccessDenied": "The key may not use the bucket. Give the R2 API token 'Object Read & Write' on it.",
    "NoSuchBucket": "There is no such bucket at the endpoint. Check the name; for an EU bucket use "
                    "https://<account-id>.eu.r2.cloudflarestorage.com.",
}


def explain(error: Exception) -> str:
    if isinstance(error, ClientError):
        code = error.response.get("Error", {}).get("Code", "")
        return f"{code}: {HINTS.get(code, error)}"
    return str(error)


def listing(s3, label: str) -> list:
    """The backups of one database, newest first, as (key, taken at, size)."""
    found = []
    for page in s3.get_paginator("list_objects_v2").paginate(Bucket=BUCKET, Prefix=f"{PREFIX}/{label}/"):
        for item in page.get("Contents", []):
            match = NAME.match(item["Key"].rsplit("/", 1)[-1])
            if match:
                taken = datetime.datetime.strptime(match.group(2), STAMP).replace(tzinfo=datetime.timezone.utc)
                found.append((item["Key"], taken, item["Size"]))
    return sorted(found, key=lambda entry: entry[1], reverse=True)


# --- Backing up ----------------------------------------------------------------------

def back_up(s3, label: str, env: dict) -> str:
    """Dumps one database, checks the archive reads back, uploads it, and prunes."""
    taken = datetime.datetime.now(datetime.timezone.utc)
    key = f"{PREFIX}/{label}/{label}-{taken.strftime(STAMP)}.dump"

    with tempfile.TemporaryDirectory() as folder:
        archive = os.path.join(folder, "backup.dump")
        dumped = run_pg(["pg_dump", "--format=custom", "--compress=6", f"--file={archive}"], env)
        if dumped.returncode != 0:
            raise RuntimeError(f"pg_dump of the {label} database failed: {dumped.stderr.strip()}")

        # A dump that cannot be listed cannot be restored either; never upload one.
        checked = subprocess.run(["pg_restore", "--list", archive], capture_output=True, text=True)
        if checked.returncode != 0:
            raise RuntimeError(f"The {label} archive does not read back: {checked.stderr.strip()}")

        size = os.path.getsize(archive)
        s3.upload_file(archive, BUCKET, key, ExtraArgs={"ContentType": "application/octet-stream"},
                       Config=TransferConfig(multipart_threshold=64 * 1024 * 1024,
                                             multipart_chunksize=64 * 1024 * 1024))

    log(f"Backed up the {label} database to s3://{BUCKET}/{key} ({size:,} bytes).")
    prune(s3, label)
    return key


def prune(s3, label: str) -> None:
    cutoff = datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(days=KEEP_DAYS)
    for key, taken, _ in listing(s3, label)[KEEP_MIN:]:
        if taken < cutoff:
            s3.delete_object(Bucket=BUCKET, Key=key)
            log(f"Deleted s3://{BUCKET}/{key}, older than {KEEP_DAYS:g} days.")


def back_up_all(s3) -> list:
    return [back_up(s3, label, env) for label, env in databases().items()]


# --- Running on a schedule -----------------------------------------------------------

STATUS = {"started": None, "last_success": None, "last_keys": [], "last_error": None,
          "next_run": None, "every_hours": EVERY / 3600}
LOCK = threading.Lock()


def iso(moment: float | None) -> str | None:
    return None if moment is None else datetime.datetime.fromtimestamp(
        moment, datetime.timezone.utc).isoformat(timespec="seconds")


class Health(BaseHTTPRequestHandler):
    """Any path answers 200 while the service runs, which is what a platform's health
    check wants. /status answers 503 when the last backup failed or one is overdue,
    which is what monitoring wants."""

    def do_GET(self):  # noqa: N802 - the name http.server looks for
        with LOCK:
            body = {key: iso(value) if key in ("started", "last_success", "next_run") else value
                    for key, value in STATUS.items()}
            overdue = STATUS["last_success"] is None or time.time() - STATUS["last_success"] > 2 * EVERY + 3600
            failing = STATUS["last_error"] is not None
            # A new service has an hour to make its first backup before it counts as overdue.
            overdue = overdue and time.time() - STATUS["started"] > 3600
        healthy = not (self.path.rstrip("/") == "/status" and (failing or overdue))
        payload = json.dumps(body).encode()
        self.send_response(200 if healthy else 503)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def log_message(self, *args):  # the platform's probes would fill the log
        pass


def check_bucket(s3) -> None:
    """Stops the start on a key or bucket problem, with what to do about it, rather than
    reporting healthy and failing every backup."""
    for attempt in range(1, 6):
        try:
            s3.list_objects_v2(Bucket=BUCKET, Prefix=f"{PREFIX}/", MaxKeys=1)
            return
        except ClientError as error:
            sys.exit(f"[backup] Cannot use s3://{BUCKET}: {explain(error)}")
        except BotoCoreError as error:
            log(f"Cannot reach {endpoint()} yet ({error}); trying again ({attempt}/5).")
            time.sleep(5 * attempt)
    sys.exit(f"[backup] Cannot reach {endpoint()}.")


def run() -> None:
    if not BUCKET:
        sys.exit("[backup] Set BACKUP_S3_BUCKET (or the API's Erp__Storage__S3__Bucket).")
    labels = databases()
    s3 = client()
    check_bucket(s3)

    with LOCK:
        STATUS["started"] = time.time()
    port = int(setting("PORT", default="8080"))
    server = ThreadingHTTPServer(("0.0.0.0", port), Health)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    stop = threading.Event()
    for signum in (signal.SIGTERM, signal.SIGINT):
        signal.signal(signum, lambda *_: stop.set())

    # Carry on from the newest backup already in the bucket, so a restart or a
    # redeploy neither backs up again at once nor skips one that is due.
    newest = [entries[0][1].timestamp() for label in labels if (entries := listing(s3, label))]
    due = min(newest) + EVERY if len(newest) == len(labels) else time.time() + 60
    with LOCK:
        STATUS["last_success"] = min(newest) if len(newest) == len(labels) else None
        STATUS["next_run"] = max(due, time.time() + 60)
    log(f"Backing up {', '.join(labels)} to s3://{BUCKET}/{PREFIX}/ every {EVERY / 3600:g} h, keeping "
        f"{KEEP_DAYS:g} days (at least {KEEP_MIN}). Health on :{port}. Next at {iso(STATUS['next_run'])}.")

    while not stop.is_set():
        wait = STATUS["next_run"] - time.time()
        if wait > 0:
            stop.wait(min(wait, 60))
            continue
        try:
            keys = back_up_all(s3)
            with LOCK:
                STATUS.update(last_success=time.time(), last_keys=keys, last_error=None,
                              next_run=time.time() + EVERY)
        except (RuntimeError, ClientError, BotoCoreError, OSError) as error:
            message = explain(error)
            log(f"Backup failed: {message}. Trying again in {RETRY / 60:g} minutes.")
            with LOCK:
                STATUS.update(last_error=message, next_run=time.time() + RETRY)

    server.shutdown()
    log("Stopped.")


# --- By hand -------------------------------------------------------------------------

def show(labels: list) -> None:
    s3 = client()
    for label in labels:
        entries = listing(s3, label)
        print(f"{label}: {len(entries)} backup(s)")
        for key, taken, size in entries:
            print(f"  {taken.isoformat(timespec='seconds')}  {size:>14,}  {key}")


SET_LINE = re.compile(r"^SET (\w+) = .*;$")


def server_settings(env: dict) -> set:
    """The settings the target server knows, so a newer pg_dump's SET lines it does not
    know (transaction_timeout, before PostgreSQL 17) can be left out of the restore."""
    found = run_pg(["psql", "-X", "-A", "-t", "-c", "SELECT name FROM pg_settings"], env)
    if found.returncode != 0:
        sys.exit(f"[backup] Cannot connect to the target database: {found.stderr.strip()}")
    return set(found.stdout.split())


def restore(label: str, which: str, target: str | None) -> None:
    s3 = client()
    if which == "latest":
        entries = listing(s3, label)
        if not entries:
            sys.exit(f"[backup] There is no {label} backup in s3://{BUCKET}/{PREFIX}/{label}/.")
        which = entries[0][0]
    env = {"PGDATABASE": target} if target else connection(label)
    if not env:
        sys.exit(f"[backup] Nothing says where the {label} database is; give --target postgres://...")
    known = server_settings(env)

    with tempfile.TemporaryDirectory() as folder:
        archive = os.path.join(folder, "restore.dump")
        log(f"Downloading s3://{BUCKET}/{which}...")
        s3.download_file(BUCKET, which, archive)
        log("Restoring; whatever the database holds now is replaced.")

        # pg_restore writes the SQL and psql runs it in one transaction that stops at the
        # first error, so a restore either completes or changes nothing.
        full = dict(os.environ, **{k: v for k, v in env.items() if k != "PGDATABASE"})
        psql = ["psql", "-X", "--quiet", "--single-transaction", "-v", "ON_ERROR_STOP=1", "--output=/dev/null"]
        if "PGDATABASE" in env:
            psql.append(f"--dbname={env['PGDATABASE']}")
        writer = subprocess.Popen(["pg_restore", "--clean", "--if-exists", "--no-owner", "--file=-", archive],
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
        reader = subprocess.Popen(psql, env=full, stdin=subprocess.PIPE, stderr=subprocess.PIPE,
                                  text=True, encoding="utf-8")
        copying = False
        try:
            for line in writer.stdout:
                if copying:
                    copying = line != "\\.\n"
                elif line.startswith("COPY ") and line.rstrip().endswith("FROM stdin;"):
                    copying = True
                elif (match := SET_LINE.match(line.rstrip("\n"))) and match.group(1) not in known:
                    continue
                reader.stdin.write(line)
            reader.stdin.close()
        except BrokenPipeError:
            pass
        failed = writer.wait() != 0, reader.wait() != 0
        problem = (writer.stderr.read() + reader.stderr.read()).strip()
    if any(failed):
        sys.exit(f"[backup] The restore failed, and changed nothing: {problem}")
    log(f"Restored {which}.")


def main(argv: list) -> None:
    command = argv[1] if len(argv) > 1 else "run"
    if command == "run":
        run()
    elif command == "now":
        if not BUCKET:
            sys.exit("[backup] Set BACKUP_S3_BUCKET (or the API's Erp__Storage__S3__Bucket).")
        s3 = client()
        check_bucket(s3)
        back_up_all(s3)
    elif command == "list":
        show(argv[2:] or ["main", "audit"])
    elif command == "restore":
        args = [a for a in argv[2:] if not a.startswith("--")]
        target = next((argv[i + 1] for i, a in enumerate(argv) if a == "--target" and i + 1 < len(argv)), None)
        args = [a for a in args if a != target]
        if len(args) != 2 or args[0] not in ("main", "audit"):
            sys.exit("usage: backup.py restore main|audit KEY|latest --yes [--target postgres://...]")
        if "--yes" not in argv:
            sys.exit("[backup] A restore replaces everything in the database. Stop the API, then run "
                     "it again with --yes.")
        restore(args[0], args[1], target)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv)
