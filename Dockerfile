# =============================================================================
# ERP.Api container image — the backend service.
#
# This file sits at the repository root because the build genuinely needs the
# whole repository as its context: the root .editorconfig carries analyzer
# severities the backend compiles against, and the image is built with warnings
# promoted to errors. A Dockerfile under backend/ could not reach it, and the
# container build would apply a different ruleset from a local one.
#
# On a deployment platform this is the service whose ROOT DIRECTORY is "."; the
# web client is a separate service with its root directory set to apps/web,
# where it has a Dockerfile of its own.
#
#   docker build -t inspire-erp/api .
# =============================================================================

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Project and props files are copied before the source so `restore` is cached on
# the dependency graph alone. A one-line change to a .cs file then rebuilds
# without re-downloading every NuGet package.
COPY backend/Directory.Build.props backend/Directory.Packages.props backend/stylecop.json backend/
COPY backend/ERP.slnx backend/
COPY backend/src/ERP.SharedKernel/ERP.SharedKernel.csproj      backend/src/ERP.SharedKernel/
COPY backend/src/ERP.Domain/ERP.Domain.csproj                  backend/src/ERP.Domain/
COPY backend/src/ERP.Application/ERP.Application.csproj        backend/src/ERP.Application/
COPY backend/src/ERP.Infrastructure/ERP.Infrastructure.csproj  backend/src/ERP.Infrastructure/
COPY backend/src/ERP.Identity/ERP.Identity.csproj              backend/src/ERP.Identity/
COPY backend/src/ERP.Reporting/ERP.Reporting.csproj            backend/src/ERP.Reporting/
COPY backend/src/ERP.Notifications/ERP.Notifications.csproj    backend/src/ERP.Notifications/
COPY backend/src/ERP.DynamicForms/ERP.DynamicForms.csproj      backend/src/ERP.DynamicForms/
COPY backend/src/ERP.PrintDesigner/ERP.PrintDesigner.csproj    backend/src/ERP.PrintDesigner/
COPY backend/src/ERP.Workflow/ERP.Workflow.csproj              backend/src/ERP.Workflow/
COPY backend/src/ERP.Api/ERP.Api.csproj                        backend/src/ERP.Api/

RUN dotnet restore backend/src/ERP.Api/ERP.Api.csproj

COPY backend/src/ backend/src/

# BOTH .editorconfig files, and the root one is not optional. Analyzer severities
# are split across the two - the repository-wide rules live at the root and the
# backend-specific ones under backend/ - so copying only one makes the container
# build apply a different ruleset from a local build. That divergence surfaces as
# CI failing on code that compiles cleanly on a developer's machine, which is
# among the more demoralising ways to lose an afternoon.
COPY .editorconfig ./
COPY backend/.editorconfig backend/

# ContinuousIntegrationBuild makes the build deterministic and turns warnings
# into errors, so the image cannot be produced from code that would fail CI.
RUN dotnet publish backend/src/ERP.Api/ERP.Api.csproj \
        --no-restore \
        --configuration Release \
        --output /app/publish \
        -p:ContinuousIntegrationBuild=true

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# ICU and time-zone data are not in the Alpine runtime image by default, and both
# are load-bearing here: the firm and branch time zones drive day-book and
# Z-report boundaries, and Arabic formatting needs full globalisation. Without
# these the container starts and then fails on the first TimeZoneInfo lookup.
RUN apk add --no-cache icu-libs icu-data-full tzdata

# ASPNETCORE_HTTP_PORTS is the fallback for a plain `docker run`. A deployment
# platform injects PORT instead, and the application binds that in preference -
# see PlatformConfiguration.ResolveListenUrl. The two agree by default so that
# EXPOSE below is accurate either way.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_HTTP_PORTS=8080 \
    PORT=8080 \
    DOTNET_gcServer=1

# A non-root user. The application needs no write access to its own files, and
# running as root would let a container escape act with far more authority.
RUN addgroup -S erp && adduser -S -G erp erp
USER erp

COPY --from=build --chown=erp:erp /app/publish .

# How the platform discovers which port to publish. Keep this in step with the
# PORT default above.
EXPOSE 8080

# Hits the liveness probe, which is dependency-free by design - so the container
# is not reported unhealthy merely because PostgreSQL is briefly unreachable.
# Shell form, so PORT is expanded at runtime rather than baked in at build.
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD wget -qO- "http://127.0.0.1:${PORT:-8080}/health/live" || exit 1

ENTRYPOINT ["dotnet", "ERP.Api.dll"]
