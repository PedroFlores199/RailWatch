FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/ src/
RUN dotnet publish src/RailWatch.Api -c Release -o /app/api
RUN dotnet publish src/RailWatch.Worker -c Release -o /app/worker

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/api ./
COPY samples/ ./samples/
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RailWatch.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=build /app/worker ./
COPY samples/ ./samples/
USER app
ENTRYPOINT ["dotnet", "RailWatch.Worker.dll"]
