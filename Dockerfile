FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Trophy.Catalogue.csproj Directory.Build.props Directory.Build.targets ./
COPY Data ./Data
RUN dotnet restore Trophy.Catalogue.csproj

COPY . .
RUN dotnet publish Trophy.Catalogue.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
CMD ["sh", "-c", "dotnet Trophy.Catalogue.dll --urls http://0.0.0.0:${PORT:-10000} --hostBuilder:reloadConfigOnChange=false"]
