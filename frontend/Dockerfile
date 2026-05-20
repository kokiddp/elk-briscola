# syntax=docker/dockerfile:1.7

# Stage 1 — build the Angular bundle. node:22-alpine matches the engines
# pin in package.json + the GitHub Actions runner.
FROM node:22-alpine@sha256:b2358485e3e33bc3a33114d2b1bdb18cdbe4df01bd2b257198eb51beb1f026c5 AS build
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
FROM nginx:alpine@sha256:8491795299c8e739b7fcc6285d531d9812ce2666e07bd3dd8db00020ad132295 AS runtime

# Drop the default site config and ship our own.
RUN rm /etc/nginx/conf.d/default.conf
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Angular's `outputPath` config writes `dist/<project>/browser/*` —
# that's the directory nginx should serve.
COPY --from=build /src/dist/elk-briscola-frontend/browser /usr/share/nginx/html

EXPOSE 80

# nginx:alpine already configures a curl-less HEALTHCHECK via the entry
# point, but we want our own probe so docker-compose can wait on it.
# Use 127.0.0.1, not localhost — busybox wget resolves localhost to ::1
# first, but nginx in this image binds 0.0.0.0:80 only, so the v6 path
# gets ECONNREFUSED and flags the container unhealthy despite the SPA
# serving fine over v4.
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD ["wget", "--quiet", "--tries=1", "--spider", "http://127.0.0.1/"]
