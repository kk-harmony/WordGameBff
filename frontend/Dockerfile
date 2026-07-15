# syntax=docker/dockerfile:1

FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
COPY tsconfig.json eslint.config.js ./
COPY packages ./packages
COPY apps/playground ./apps/playground
COPY scripts ./scripts
COPY types ./types

RUN npm ci
RUN npm run build
RUN npm run sri
RUN find dist -name '*.map' -delete

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/embed /usr/share/nginx/html

EXPOSE 80
HEALTHCHECK CMD wget -qO- http://127.0.0.1/health || exit 1
