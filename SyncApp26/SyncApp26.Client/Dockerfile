# syntax=docker/dockerfile:1
# Build context: this folder (self-contained).

########################
# Build stage
########################
FROM node:22-alpine AS build
WORKDIR /app

# Lockfile-only copy first, for npm ci layer caching.
COPY package.json package-lock.json ./
# --legacy-peer-deps: typescript ^6.0.3 conflicts with @angular/build's peer range (<6.0).
RUN npm ci --legacy-peer-deps

COPY . .
RUN npm run build -- --configuration production

########################
# Serve stage
########################
FROM nginx:1.27-alpine AS runtime

# Angular 21 nests build output under browser/.
COPY --from=build /app/dist/sync-app26.client/browser /usr/share/nginx/html

COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
    CMD wget -qO- http://127.0.0.1/ >/dev/null 2>&1 || exit 1
