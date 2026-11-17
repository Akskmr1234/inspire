"""Keeps privacyIDEA's SQLite database in S3-compatible storage (Cloudflare R2).

    s3sync.py restore            download the database, if the bucket has one
    s3sync.py run -- CMD [ARG]   run CMD and upload the database whenever it changes

The container then needs no volume: the database is fetched on start, and a
consistent copy (SQLite's online backup, safe while privacyIDEA writes) is
uploaded within PI_S3_SYNC_SECONDS of each change and once more on shutdown.
One dated copy per day is kept beside it as a backup.

Settings (environment):
    PI_S3_ENDPOINT           e.g. https://<account-id>.r2.cloudflarestorage.com
    PI_S3_BUCKET             bucket name
    PI_S3_ACCESS_KEY_ID      R2 API token's access key id
    PI_S3_SECRET_ACCESS_KEY  R2 API token's secret
    PI_S3_KEY                object key, default privacyidea/privacyidea.sqlite
    PI_S3_REGION             default auto (what R2 expects)
    PI_S3_SYNC_SECONDS       default 5

Only one container may use the same object at a time: two would overwrite each
other's uploads.
"""

import datetime
import os
import re
import signal
import sqlite3
import subprocess
import sys
import tempfile
import time
from urllib.parse import urlsplit

import boto3
from botocore.config import Config
from botocore.exceptions import BotoCoreError, ClientError

DATA = os.environ.get("PI_DATA", "/data")
DB = os.path.join(DATA, "privacyidea.sqlite")
KEY = os.environ.get("PI_S3_KEY", "privacyidea/privacyidea.sqlite").strip("/")
def setting(name: str, default: str = "") -> str:
    """An environment value without the spaces, line breaks or quotes a paste brings."""
    return os.environ.get(name, default).strip().strip("'\"").strip()


BUCKET = setting("PI_S3_BUCKET")
INTERVAL = max(1.0, float(os.environ.get("PI_S3_SYNC_SECONDS", "5")))


def log(message: str) -> None:
    print(f"[s3sync] {message}", file=sys.stderr, flush=True)


def endpoint() -> str:
    """The endpoint's scheme and host. Cloudflare shows the S3 address with the
    bucket on the end (https://<id>.r2.cloudflarestorage.com/<bucket>); the bucket
    is given separately, so a path pasted here is dropped."""
    raw = setting("PI_S3_ENDPOINT")
    parts = urlsplit(raw)
    if parts.path not in ("", "/"):
        log(f"Ignoring the path '{parts.path}' on PI_S3_ENDPOINT: the bucket comes from PI_S3_BUCKET.")
    return f"{parts.scheme}://{parts.netloc}"


def client():
    return boto3.client(
        "s3",
        endpoint_url=endpoint(),
        aws_access_key_id=setting("PI_S3_ACCESS_KEY_ID"),
        aws_secret_access_key=setting("PI_S3_SECRET_ACCESS_KEY"),
        region_name=setting("PI_S3_REGION", "auto") or "auto",
        config=Config(retries={"max_attempts": 5, "mode": "standard"}, s3={"addressing_style": "path"}),
    )


def restore() -> None:
    """Fetch the database. Missing object = first start; any other failure stops the start."""
    if os.path.exists(DB):
        log(f"{DB} already present; not downloading.")
        return

    s3 = client()
    try:
        s3.head_object(Bucket=BUCKET, Key=KEY)
    except ClientError as error:
        code = error.response.get("Error", {}).get("Code")
        if code in ("403", "400"):
            # A HEAD response has no body, so it cannot say why. Ask again with a
            # GET, whose error names the cause.
            code = explain(s3)
        if code in ("404", "NoSuchKey", "NotFound"):
            log(f"s3://{BUCKET}/{KEY} does not exist yet: starting with a new database.")
            return
        if code != "Readable":
            sys.exit(f"[s3sync] Cannot read s3://{BUCKET}/{KEY}: {error}. Not starting, so "
                     "an empty database never replaces the stored one.")
    except BotoCoreError as error:
        sys.exit(f"[s3sync] Cannot reach {endpoint()}: {error}. Not starting.")

    fd, partial = tempfile.mkstemp(dir=DATA, suffix=".download")
    os.close(fd)
    try:
        s3.download_file(BUCKET, KEY, partial)
        with sqlite3.connect(partial) as check:
            result = check.execute("PRAGMA integrity_check").fetchone()[0]
        if result != "ok":
            sys.exit(f"[s3sync] The downloaded database failed its integrity check ({result}). "
                     "Not starting. A daily copy is kept under the same key's daily/ prefix.")
        os.replace(partial, DB)
        log(f"Restored {DB} from s3://{BUCKET}/{KEY} ({os.path.getsize(DB)} bytes).")
    finally:
        if os.path.exists(partial):
            os.remove(partial)


HINTS = {
    "InvalidAccessKeyId": "PI_S3_ACCESS_KEY_ID is not a known key. Use the S3 'Access Key ID' "
                          "shown when the R2 API token was created, not the token value.",
    "SignatureDoesNotMatch": "PI_S3_SECRET_ACCESS_KEY does not match the key id. Use the S3 "
                             "'Secret Access Key' shown when the R2 API token was created, "
                             "from the same token as the key id.",
    "Unauthorized": "The credentials were rejected. Use the S3 'Access Key ID' and 'Secret "
                    "Access Key' of an R2 API token, not the token value.",
    "AccessDenied": f"The key is valid but may not read bucket '{BUCKET}'. Give the R2 API "
                    f"token 'Object Read & Write' on that bucket.",
    "NoSuchBucket": f"There is no bucket '{BUCKET}' at PI_S3_ENDPOINT. Check the name, and for "
                    "an EU bucket use https://<account-id>.eu.r2.cloudflarestorage.com.",
}


def shape() -> str:
    """For R2, how the keys compare with the shape R2 issues: a 32-character key id
    and a 64-character secret, both hexadecimal. Lengths only - never the values."""
    if "r2.cloudflarestorage.com" not in endpoint():
        return ""
    key, secret = setting("PI_S3_ACCESS_KEY_ID"), setting("PI_S3_SECRET_ACCESS_KEY")
    hexadecimal = re.compile(r"[0-9a-f]+")
    notes = []
    if len(key) != 32 or not hexadecimal.fullmatch(key):
        notes.append(f"the key id is {len(key)} characters, not R2's 32 hexadecimal")
    if len(secret) != 64 or not hexadecimal.fullmatch(secret):
        notes.append(f"the secret is {len(secret)} characters, not R2's 64 hexadecimal"
                     + (" - that looks like the token value" if len(secret) == 40 else ""))
    return f" ({'; '.join(notes)}.)" if notes else " (Both have R2's shape, so check they come from the same token.)"


def explain(s3) -> str:
    """Repeats a refused HEAD as a one-byte GET and reports the error code it names."""
    try:
        s3.get_object(Bucket=BUCKET, Key=KEY, Range="bytes=0-0")
        return "Readable"
    except ClientError as error:
        code = error.response.get("Error", {}).get("Code", "")
        if code in HINTS:
            sys.exit(f"[s3sync] {code}: {HINTS[code]}{shape()} Not starting.")
        return code


def marker():
    """Changes whenever SQLite writes: the database file or its journal."""
    parts = []
    for path in (DB, DB + "-wal", DB + "-journal"):
        try:
            st = os.stat(path)
            parts.append((st.st_mtime_ns, st.st_size))
        except FileNotFoundError:
            parts.append(None)
    return tuple(parts)


class Uploader:
    def __init__(self) -> None:
        self.s3 = client()
        self.uploaded = None
        self.daily = None

    def sync(self, force: bool = False) -> None:
        if not os.path.exists(DB):
            return
        current = marker()
        if current == self.uploaded and not force:
            return

        fd, snapshot = tempfile.mkstemp(dir=DATA, suffix=".upload")
        os.close(fd)
        try:
            # A consistent copy even while privacyIDEA is writing.
            with sqlite3.connect(DB, timeout=30) as source, sqlite3.connect(snapshot) as target:
                source.backup(target)
            self.s3.upload_file(snapshot, BUCKET, KEY)
            self.uploaded = current

            today = datetime.date.today().isoformat()
            if self.daily != today:
                daily_key = f"{KEY.rsplit('/', 1)[0] + '/' if '/' in KEY else ''}daily/{today}.sqlite"
                self.s3.upload_file(snapshot, BUCKET, daily_key)
                self.daily = today
            log(f"Uploaded to s3://{BUCKET}/{KEY}.")
        except (BotoCoreError, ClientError, sqlite3.Error, OSError) as error:
            # Keep serving; the next tick tries again.
            log(f"Upload failed, will retry: {error}")
        finally:
            if os.path.exists(snapshot):
                os.remove(snapshot)


def run(command: list[str]) -> None:
    uploader = Uploader()
    uploader.sync(force=True)  # what start-up created (tables, admin) is stored at once

    child = subprocess.Popen(command)
    stopping = False

    def forward(signum, _frame):
        nonlocal stopping
        stopping = True
        child.send_signal(signum)

    signal.signal(signal.SIGTERM, forward)
    signal.signal(signal.SIGINT, forward)

    while True:
        try:
            code = child.wait(timeout=INTERVAL)
            break
        except subprocess.TimeoutExpired:
            if not stopping:
                uploader.sync()

    uploader.sync(force=True)
    log("Final upload done; exiting.")
    sys.exit(code)


if __name__ == "__main__":
    if sys.argv[1:2] == ["restore"]:
        restore()
    elif sys.argv[1:3] == ["run", "--"] and len(sys.argv) > 3:
        run(sys.argv[3:])
    else:
        sys.exit("usage: s3sync.py restore | run -- CMD [ARG...]")
