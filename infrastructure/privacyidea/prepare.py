"""Start-up chores the shell entrypoint hands over to Python.

    prepare.py enckey    write PI_ENCKEY (base64, 96 bytes) to $PI_DATA/enckey
    prepare.py database  create the PostgreSQL database in PI_DATABASE_URI if missing

Secrets are read from the environment, never from the command line, so they do
not show up in process listings.
"""

import base64
import binascii
import os
import re
import sys

DATA = os.environ.get("PI_DATA", "/data")


def enckey() -> None:
    try:
        key = base64.b64decode(os.environ["PI_ENCKEY"].strip(), validate=True)
    except (binascii.Error, ValueError):
        sys.exit("PI_ENCKEY is not valid base64. Make one with: openssl rand -base64 96")
    if len(key) != 96:
        sys.exit(
            f"PI_ENCKEY decodes to {len(key)} bytes; it must be 96. "
            "Make one with: openssl rand -base64 96"
        )

    path = os.path.join(DATA, "enckey")
    if os.path.exists(path):
        with open(path, "rb") as existing:
            if existing.read() == key:
                return
        # A different key on a volume means the database was encrypted with that
        # one: replacing it would make every enrolled factor unreadable.
        sys.exit(
            f"{path} already holds a different encryption key than PI_ENCKEY. "
            "Use that key (base64 of the file) or remove one of them deliberately."
        )

    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o400)
    with os.fdopen(fd, "wb") as out:
        out.write(key)


def database() -> None:
    uri = os.environ.get("PI_DATABASE_URI", "")
    if not uri.startswith("postgresql"):
        return

    from sqlalchemy import create_engine, text
    from sqlalchemy.engine import make_url
    from sqlalchemy.exc import OperationalError, SQLAlchemyError

    url = make_url(uri)
    try:
        create_engine(url).connect().close()
        return
    except OperationalError as error:
        if "does not exist" not in str(error):
            sys.exit(f"Cannot reach the database in PI_DATABASE_URI: {error.orig}")

    name = url.database or ""
    if not re.fullmatch(r"[A-Za-z0-9_]+", name):
        sys.exit(f"Will not create a database named {name!r}: use letters, digits and _.")

    last = None
    for maintenance in ("postgres", "template1"):
        try:
            engine = create_engine(url.set(database=maintenance), isolation_level="AUTOCOMMIT")
            with engine.connect() as connection:
                connection.execute(text(f'CREATE DATABASE "{name}"'))
            print(f"Created database {name}.")
            return
        except SQLAlchemyError as error:
            last = error

    sys.exit(
        f"Database {name} does not exist and could not be created ({last}). "
        "Create it once by hand, or point PI_DATABASE_URI at an existing empty database."
    )


if __name__ == "__main__":
    {"enckey": enckey, "database": database}[sys.argv[1]]()
