ARG NODE_BUILD_IMAGE=node:24.22.3-trixie-slim
ARG NGINX_RUNTIME_IMAGE=ghcr.io/nginx/nginx-unprivileged:1.30.8-alpine3.24-slim
FROM ${NODE_BUILD_IMAGE} AS build

WORKDIR /app

RUN npm install --global --ignore-scripts --no-audit --no-fund npm@11.21.3
COPY package.json package-lock.json ./

RUN npm ci

COPY . .

ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

RUN npm run build

FROM ${NGINX_RUNTIME_IMAGE} AS runtime

USER root
RUN apk upgrade --no-cache libcrypto3 libssl3 zlib
USER 101

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080
