#!/bin/sh
# Prepares privacyIDEA on first start, then serves it.
#
# Everything privacyIDEA must keep lives in /data (a volume): its configuration,
# its encryption key, its audit signing keys and - unless PI_DATABASE_URI points
# elsewhere - its SQLite database. Losing /data loses every enrolled factor, so
# back it up with the ERP's own data.
set -eu

# Started as root: make the volume ours, then run everything else as the
# unprivileged privacyidea user.
if [ "$(id -u)" = "0" ]; then
    chown -R privacyidea:privacyidea /data
    export HOME=/data
    exec setpriv --reuid=privacyidea --regid=privacyidea --init-groups "$0" "$@"
fi

: "${PI_ADMIN_USER:=admin}"
: "${PI_ADMIN_PASSWORD:?Set PI_ADMIN_PASSWORD}"
: "${PI_SECRET_KEY:?Set PI_SECRET_KEY to a long random string}"
: "${PI_PEPPER:?Set PI_PEPPER to a long random string}"
: "${PI_DATABASE_URI:=sqlite:////data/privacyidea.sqlite}"
: "${PI_WORKERS:=2}"

PI=/opt/privacyidea/bin/pi-manage

if [ ! -f /data/pi.cfg ]; then
    cat > /data/pi.cfg <<CFG
SQLALCHEMY_DATABASE_URI = '${PI_DATABASE_URI}'
SECRET_KEY = '${PI_SECRET_KEY}'
PI_PEPPER = '${PI_PEPPER}'
PI_ENCFILE = '/data/enckey'
PI_AUDIT_KEY_PRIVATE = '/data/private.pem'
PI_AUDIT_KEY_PUBLIC = '/data/public.pem'
PI_LOGFILE = '/data/privacyidea.log'
PI_LOGLEVEL = 20
SUPERUSER_REALM = ['administrator']
CFG
    chmod 600 /data/pi.cfg
fi

[ -f /data/enckey ] || "$PI" setup create_enckey
[ -f /data/private.pem ] || "$PI" setup create_audit_keys

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
