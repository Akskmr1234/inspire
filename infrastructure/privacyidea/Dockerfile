# privacyIDEA - the free, open-source (AGPL) two-factor server the ERP uses for
# authenticator apps, push approval and backup codes.
#
# Built from the official PyPI release rather than a third-party image, so the
# version is the one the ERP was tested against and nothing else rides along.
FROM python:3.12-slim-bookworm

ARG PI_VERSION=3.14

ENV PI_HOME=/opt/privacyidea \
    PI_DATA=/data \
    PRIVACYIDEA_CONFIGFILE=/opt/privacyidea/pi.cfg \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

RUN python -m venv "$PI_HOME" \
    && "$PI_HOME/bin/pip" install --no-cache-dir --upgrade pip \
    && "$PI_HOME/bin/pip" install --no-cache-dir "privacyIDEA==${PI_VERSION}" gunicorn psycopg2-binary \
    && useradd --system --home "$PI_DATA" --shell /usr/sbin/nologin privacyidea \
    && mkdir -p "$PI_DATA" \
    && chown privacyidea "$PI_DATA"

# Plain COPY + chmod (not COPY --chmod) so the image also builds without BuildKit.
COPY entrypoint.sh /usr/local/bin/privacyidea-entrypoint
COPY app.py pi.cfg prepare.py "$PI_HOME/"
RUN chmod 0755 /usr/local/bin/privacyidea-entrypoint

# Starts as root only to take ownership of /data - hosting platforms often mount
# volumes owned by root - then the entrypoint drops to the privacyidea user.
WORKDIR /data
VOLUME ["/data"]
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=5 \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8080/healthcheck/', timeout=4)" || exit 1

ENTRYPOINT ["/usr/local/bin/privacyidea-entrypoint"]
