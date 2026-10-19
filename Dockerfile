# ビルドと実行をLinux amd64に固定し、公式イメージもダイジェストで固定。
FROM node:24.16.0-bookworm-slim@sha256:ca520832af80fa37a57c14077ed0fcdd83b5aefccc356059fdc3a9a05b78ae1f AS node
FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:3a48280a9a7c6276c3eeaba1efa6a08820f18c84fb9e6274c187719ecfaf9fbc AS build
ARG TARGETARCH
RUN test "$TARGETARCH" = "amd64"
COPY --from=node /usr/local/ /usr/local/
WORKDIR /source
COPY global.json ./
COPY src/GoodsManager.Web/ src/GoodsManager.Web/
COPY src/GoodsManager.Client/ src/GoodsManager.Client/
RUN dotnet publish src/GoodsManager.Web -c Release -r linux-x64 --self-contained false -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS runtime
ARG TARGETARCH
RUN test "$TARGETARCH" = "amd64" && mkdir -p /data /backups /logs && chown 1654:1654 /data /backups /logs
WORKDIR /app
COPY --from=build /publish/ ./
ENV ASPNETCORE_HTTP_PORTS=8080 ASPNETCORE_ENVIRONMENT=Production \
    Storage__Directory=/data Backups__Directory=/backups Operations__LogDirectory=/logs \
    DOTNET_EnableDiagnostics=0
USER 1654:1654
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=10s --start-period=120s --retries=3 CMD ["dotnet", "GoodsManager.Web.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "GoodsManager.Web.dll"]
