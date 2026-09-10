FROM mcr.microsoft.com/devcontainers/dotnet:10.0-noble@sha256:9eb314432a5fa67773a53ab754cdc381cae614906162f9fc44cdb7f7837b91c7

RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
        jq \
        httpie \
    && apt-get clean -y \
    && rm -rf /var/lib/apt/lists/*

USER vscode
RUN dotnet tool install --global dotnet-ef \
    && dotnet tool install --global dotnet-outdated-tool \
    && dotnet tool install --global dotnet-reportgenerator-globaltool

# Ensure .NET tools are in PATH
ENV PATH="${PATH}:/home/vscode/.dotnet/tools"

# Set working directory
WORKDIR /workspaces
