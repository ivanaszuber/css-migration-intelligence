# File purpose: Builds Angular and .NET separately, then packages both into the ASP.NET runtime image deployed to Azure.
FROM node:20-alpine AS web-build
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api-build
WORKDIR /source
COPY . .
RUN dotnet publish src/CssMigration.Intelligence.Api/CssMigration.Intelligence.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=api-build /app .
COPY --from=web-build /web/dist/css-intelligence ./wwwroot
COPY samples /samples
ENV ASPNETCORE_URLS=http://+:8080
ENV Portfolio__JsonPath=/samples/portfolio.manifest.json
EXPOSE 8080
ENTRYPOINT ["dotnet", "CssMigration.Intelligence.Api.dll"]
