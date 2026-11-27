ARG NODE_BUILD_IMAGE=node:24-bookworm-slim
ARG NGINX_RUNTIME_IMAGE=nginxinc/nginx-unprivileged:stable-alpine-slim
FROM ${NODE_BUILD_IMAGE} AS build

WORKDIR /app

COPY package.json package-lock.json ./

RUN npm ci

COPY . .

ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

RUN npm run build

FROM ${NGINX_RUNTIME_IMAGE} AS runtime

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080
