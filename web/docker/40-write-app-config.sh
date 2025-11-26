#!/bin/sh
# Writes the runtime configuration the Angular app loads at start-up.
# API_BASE_URL is empty by default, meaning "same origin" (proxied by nginx).
set -eu
printf '{\n  "apiBaseUrl": "%s"\n}\n' "${API_BASE_URL:-}" > /usr/share/nginx/html/config.json
