# Build from the repository root:  docker build -f samples/CrudApp/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json .
COPY src/RedisCrud/RedisCrud.csproj src/RedisCrud/
COPY samples/CrudApp/CrudApp.csproj samples/CrudApp/
RUN dotnet restore samples/CrudApp/CrudApp.csproj

COPY src/ src/
COPY samples/ samples/
RUN dotnet publish samples/CrudApp/CrudApp.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "CrudApp.dll"]
