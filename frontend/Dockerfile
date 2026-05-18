# syntax=docker/dockerfile:1.7

# Stage 1 — build the Angular bundle. node:22-alpine matches the engines
# pin in package.json + the GitHub Actions runner.
FROM node:22-alpine AS build
WORKDIR /src

# Lock + manifest first for the install cache layer.
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

# Everything else, then production build.
COPY . .
RUN npm run build

# Stage 2 — static-file server. nginx:alpine is ~12 MB before our payload
# and ships gzip + brotli-less defaults. We override the default site
# with a SPA-aware config (try_files fallback, long-cache hashed assets,
# no-cache on index.html).
FROM nginx:alpine AS runtime

# Drop the default site config and ship our own.
RUN rm /etc/nginx/conf.d/default.conf
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Angular's `outputPath` config writes `dist/<project>/browser/*` —
# that's the directory nginx should serve.
COPY --from=build /src/dist/elk-briscola-frontend/browser /usr/share/nginx/html

EXPOSE 80

# nginx:alpine already configures a curl-less HEALTHCHECK via the entry
# point, but we want our own probe so docker-compose can wait on it.
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost/ || exit 1
