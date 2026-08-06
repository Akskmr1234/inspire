# =============================================================================
# Inspire ERP web client — static bundle served by nginx.
#
# On a deployment platform this is the service whose ROOT DIRECTORY is apps/web,
# which is also this build's context. The API is a separate service built from
# the repository root.
#
#   docker build -t inspire-erp/web --build-arg VITE_API_URL=https://… apps/web
# =============================================================================

# ---------- build ----------
FROM node:22-alpine AS build
WORKDIR /app

# VITE_API_URL is inlined into the JavaScript bundle by Vite at build time. It
# cannot be changed afterwards by setting an environment variable on the running
# container: by then the value is already compiled into the shipped assets.
#
# Deliberately declared with NO default. A default such as http://localhost:5199
# would let the build succeed while producing a bundle that tells every user's
# browser to call their own machine - a failure invisible in the build log, in
# the container log, and in any health check, and visible only as an application
# that loads and then does nothing. Left unset the client calls its own origin,
# which is correct when a single reverse proxy fronts both services.
ARG VITE_API_URL
ENV VITE_API_URL=$VITE_API_URL

# The manifest is copied first so the dependency install is cached against it
# alone, and editing a component does not reinstall node_modules.
#
# `npm install` rather than `npm ci`: this workspace's lockfile lives at the
# repository root, outside this build context. The alternative - building from
# the repository root - would make every backend change invalidate the web
# image's cache, for a lockfile.
COPY package.json ./
RUN npm install --no-audit --no-fund

COPY . .

# `npm run build` runs `tsc -b` before Vite, so a type error fails the image
# rather than shipping a bundle that breaks in a browser.
RUN npm run build

# ---------- runtime ----------
FROM nginx:1.27-alpine AS runtime

# The nginx entrypoint runs envsubst over everything in this directory and
# writes the result into conf.d.
COPY nginx.conf.template /etc/nginx/templates/default.conf.template

# Restricts substitution to PORT alone. Without the filter, envsubst would also
# replace nginx's own runtime variables - $uri, $host - with empty strings, and
# nginx would start with a configuration that serves nothing. The default here
# keeps a plain `docker run` working; a platform injecting PORT overrides it.
ENV NGINX_ENVSUBST_FILTER="^PORT$" \
    PORT=80

COPY --from=build /app/dist /usr/share/nginx/html

# How the platform discovers which port to publish. Keep this in step with the
# PORT default above.
EXPOSE 80

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -qO- "http://localhost:${PORT:-80}/" >/dev/null || exit 1
