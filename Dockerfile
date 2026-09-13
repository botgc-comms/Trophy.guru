FROM node:24-bookworm-slim AS assets
WORKDIR /assets-build
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY scripts/minify-assets.cjs ./scripts/minify-assets.cjs
COPY wwwroot ./wwwroot
RUN node scripts/minify-assets.cjs --outdir /assets

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Trophy.Catalogue.csproj Directory.Build.props Directory.Build.targets ./
COPY Data ./Data
RUN dotnet restore Trophy.Catalogue.csproj

COPY . .
RUN dotnet publish Trophy.Catalogue.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false /p:SkipAssetMinification=true

COPY --from=assets /assets/ /app/publish/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
CMD ["sh", "-c", "dotnet Trophy.Catalogue.dll --urls http://0.0.0.0:${PORT:-10000} --hostBuilder:reloadConfigOnChange=false"]
