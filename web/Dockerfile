# File purpose: Builds the Angular client and serves it from Nginx for the legacy two-container local setup.
FROM node:20-alpine AS build
WORKDIR /source
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:1.27-alpine
COPY --from=build /source/dist/css-intelligence /usr/share/nginx/html
EXPOSE 80
