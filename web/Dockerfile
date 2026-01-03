FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
RUN npm run build

FROM nginx:1.27-alpine AS runtime
# The nginx image renders templates with the environment at start-up; only variables that are
# set (here API_UPSTREAM) are substituted, so nginx's own $variables are left alone.
ENV API_UPSTREAM=http://api:8080
COPY nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/web/browser /usr/share/nginx/html
# Scripts in /docker-entrypoint.d run before nginx starts.
COPY --chmod=755 docker/40-write-app-config.sh /docker-entrypoint.d/40-write-app-config.sh
EXPOSE 80
