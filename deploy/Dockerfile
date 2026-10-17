# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM denoland/deno:2.9.7@sha256:fa335acdf6b72106eda2cb6a8cb5f4187e7630e357467489db4b2e7352d5e432 AS frontend
USER root
RUN apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/*
WORKDIR /source
COPY web/deno.json web/deno.lock web/
RUN deno install --config web/deno.json --frozen
COPY web/ web/
COPY scripts/import-web-content.py scripts/
COPY prototype/ prototype/
COPY plugins/ plugins/
COPY core/ core/
RUN deno task --cwd web build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS backend
WORKDIR /source
COPY global.json ./
COPY eng/ eng/
COPY src/ src/
RUN dotnet restore src/FreeTrain.Server/FreeTrain.Server.csproj --locked-mode \
 && dotnet publish src/FreeTrain.Server/FreeTrain.Server.csproj -c Release --no-restore -p:UseAppHost=false -o /release

# Portable managed publish; the target image supplies the Linux x64 runtime.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS release
WORKDIR /app
COPY --from=backend /release/ ./
COPY --from=frontend /source/web/dist/ wwwroot/
RUN mkdir -p /data/saves && chown -R app:app /data
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    FreeTrain__Saves__Directory=/data/saves \
    DOTNET_EnableDiagnostics=0
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "FreeTrain.Server.dll"]
