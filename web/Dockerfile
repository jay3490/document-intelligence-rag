FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/web/browser /usr/share/nginx/html
# Scripts in /docker-entrypoint.d run before nginx starts.
COPY --chmod=755 docker/40-write-app-config.sh /docker-entrypoint.d/40-write-app-config.sh
EXPOSE 80
