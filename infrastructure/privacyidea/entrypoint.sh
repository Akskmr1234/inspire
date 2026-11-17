#!/bin/sh
# Prepares privacyIDEA, then serves it.
#
# Two ways to run it:
#
#  - Without a volume (recommended on a hosting platform): set PI_ENCKEY and point
#    PI_DATABASE_URI at PostgreSQL. Everything privacyIDEA keeps is then in that
#    database, and the container can be replaced at will.
#  - With a volume on /data: leave PI_ENCKEY unset. The encryption key, audit keys
#    and (by default) a SQLite database are created there once. Losing /data then
#    loses every enrolled factor.
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

if [ -n "${PI_ENCKEY:-}" ]; then
    "$PY" /opt/privacyidea/prepare.py enckey
    export PI_AUDIT_NO_SIGN=1
    case "${PI_DATABASE_URI:-sqlite}" in
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

# Creates any missing tables and leaves existing ones alone, so a restart or an
# upgrade of the image is safe.
"$PI" setup create_tables

if ! "$PI" admin list 2>/dev/null | grep -q "^${PI_ADMIN_USER}\b"; then
    "$PI" admin add "$PI_ADMIN_USER" -p "$PI_ADMIN_PASSWORD"
fi

exec /opt/privacyidea/bin/gunicorn \
    --bind 0.0.0.0:8080 \
    --workers "$PI_WORKERS" \
    --chdir /opt/privacyidea \
    --access-logfile - \
    app:application
