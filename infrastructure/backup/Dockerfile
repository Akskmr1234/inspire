# Backs the ERP's PostgreSQL databases up to Cloudflare R2 (or any S3-compatible
# storage) on a schedule. A separate, optional service: the API and its image are
# unchanged, and nothing else needs it to run.
#
# Built on the official PostgreSQL image for its pg_dump and pg_restore. They must be
# at least as new as the database server; 18 reads every server from 9.2 on.
ARG PG_MAJOR=18
FROM postgres:${PG_MAJOR}-alpine

RUN apk add --no-cache python3 py3-boto3

# Plain COPY (no --chmod) so the image also builds without BuildKit.
COPY backup.py /opt/backup/backup.py

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PORT=8080

# Dumps go to a temporary folder and are deleted once uploaded; nothing is kept here,
# so the container needs no volume.
USER postgres
WORKDIR /tmp
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=5 \
    CMD wget -qO /dev/null "http://127.0.0.1:${PORT:-8080}/" || exit 1

ENTRYPOINT ["python3", "/opt/backup/backup.py"]
CMD ["run"]
