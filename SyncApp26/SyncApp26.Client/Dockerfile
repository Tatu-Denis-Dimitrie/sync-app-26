# syntax=docker/dockerfile:1
# Build context is this folder (SyncApp26.Client/) — self-contained, no sibling project references.

########################
# Build stage
########################
FROM node:22-alpine AS build
WORKDIR /app

# Lockfile-only copy first so `npm ci` is cached across source-only changes.
COPY package.json package-lock.json ./
# --legacy-peer-deps: package.json pins typescript ^6.0.3, but @angular/build@21.2.10 peer-depends
# on typescript >=5.9 <6.0 — a pre-existing conflict `npm ci`'s strict resolver rejects outright.
RUN npm ci --legacy-peer-deps

COPY . .
RUN npm run build -- --configuration production

########################
# Serve stage
########################
FROM nginx:1.27-alpine AS runtime

# Angular 21's @angular/build:application builder nests real static output one level deeper,
# under a "browser" subfolder — confirmed against an actual local build.
COPY --from=build /app/dist/sync-app26.client/browser /usr/share/nginx/html

COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
    CMD wget -qO- http://127.0.0.1/ >/dev/null 2>&1 || exit 1
