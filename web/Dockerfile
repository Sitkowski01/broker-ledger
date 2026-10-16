# Obraz frontu: build Angulara w Node, serwowanie przez nginx. /api idzie do kontenera API.
#   docker compose --profile app up --build
FROM node:24-alpine AS build
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts
COPY . .
RUN npx ng build

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist/broker-web/browser /usr/share/nginx/html
EXPOSE 80
