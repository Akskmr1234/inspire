#!/bin/sh
# Prepares privacyIDEA, then serves it.
#
# Ways to run it:
#
#  - SQLite kept in S3 / Cloudflare R2 (no volume): set PI_ENCKEY and the PI_S3_*
#    variables. The database is downloaded on start and uploaded as it changes
#    (s3sync.py), so the container can be replaced at will.
#  - PostgreSQL (no volume): set PI_ENCKEY and point PI_DATABASE_URI at it.
#  - A volume on /data: leave PI_ENCKEY unset. The encryption key, audit keys and
#    a SQLite database are created there once. Losing /data loses every factor.
set -eu

# Started as root: make /data ours, then run everything else as the unprivileged
# privacyidea user.
if [ "$(id -u)" = "0" ]; then
    chown -R privacyidea:privacyidea /data
    export HOME=/data
    exec setpriv --reuid=privacyidea --regid=privacyidea --init-groups "$0" "$@"
fi

: "${PI_ADMIN_USER:=admin}"
: "${PI_ADMIN_PASSWORD:?Set PI_ADMIN_PASSWORD}"
: "${PI_SECRET_KEY:?Set PI_SECRET_KEY to a long random string}"
: "${PI_PEPPER:?Set PI_PEPPER to a long random string}"
: "${PI_WORKERS:=2}"

# Hosting platforms hand out postgres:// URLs, which SQLAlchemy no longer accepts,
# and a bare postgresql:// now means a driver this image does not ship: name the
# installed one (psycopg2) explicitly.
if [ -n "${PI_DATABASE_URI:-}" ]; then
    case "$PI_DATABASE_URI" in
        postgres://*) PI_DATABASE_URI="postgresql+psycopg2://${PI_DATABASE_URI#postgres://}" ;;
        postgresql://*) PI_DATABASE_URI="postgresql+psycopg2://${PI_DATABASE_URI#postgresql://}" ;;
    esac
    export PI_DATABASE_URI
fi

PY=/opt/privacyidea/bin/python
PI=/opt/privacyidea/bin/pi-manage

S3=""
if [ -n "${PI_S3_BUCKET:-}" ]; then
    S3=1
    : "${PI_S3_ENDPOINT:?Set PI_S3_ENDPOINT, e.g. https://<account-id>.r2.cloudflarestorage.com}"
    : "${PI_S3_ACCESS_KEY_ID:?Set PI_S3_ACCESS_KEY_ID}"
    : "${PI_S3_SECRET_ACCESS_KEY:?Set PI_S3_SECRET_ACCESS_KEY}"
    : "${PI_ENCKEY:?Set PI_ENCKEY: with the database in S3 there is no volume to keep the key in}"
    if [ -n "${PI_DATABASE_URI:-}" ]; then
        echo "PI_S3_* keeps the SQLite database in S3; unset PI_DATABASE_URI." >&2
        exit 1
    fi
fi

if [ -n "${PI_ENCKEY:-}" ]; then
    "$PY" /opt/privacyidea/prepare.py enckey
    export PI_AUDIT_NO_SIGN=1
    case "${S3:-}${PI_DATABASE_URI:-sqlite}" in
        sqlite*) echo "WARNING: PI_ENCKEY is set but the database is SQLite in /data." \
                      "Without a volume, enrolled factors are lost on restart." \
                      "Set PI_DATABASE_URI to PostgreSQL." >&2 ;;
    esac
else
    [ -f /data/enckey ] || "$PI" setup create_enckey
    [ -f /data/private.pem ] || "$PI" setup create_audit_keys
    case "${PI_DATABASE_URI:-sqlite}" in
        sqlite*) ;;
        *) echo "NOTE: PI_ENCKEY is not set, so the encryption key lives in /data." \
                "/data must be a persistent volume, or set PI_ENCKEY." >&2 ;;
    esac
fi

"$PY" /opt/privacyidea/prepare.py database
[ -z "$S3" ] || "$PY" /opt/privacyidea/s3sync.py restore

# Creates any missing tables and leaves existing ones alone, so a restart or an
# upgrade of the image is safe.
"$PI" setup create_tables

if ! "$PI" admin list 2>/dev/null | grep -q "^${PI_ADMIN_USER}\b"; then
    "$PI" admin add "$PI_ADMIN_USER" -p "$PI_ADMIN_PASSWORD"
fi

# With S3, s3sync runs gunicorn and uploads the database as it changes.
set -- /opt/privacyidea/bin/gunicorn \
    --bind 0.0.0.0:8080 \
    --workers "$PI_WORKERS" \
    --chdir /opt/privacyidea \
    --access-logfile - \
    app:application

if [ -n "$S3" ]; then
    exec "$PY" /opt/privacyidea/s3sync.py run -- "$@"
fi
exec "$@"
