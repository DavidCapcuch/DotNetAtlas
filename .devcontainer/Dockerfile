FROM mcr.microsoft.com/devcontainers/dotnet:10.0-noble@sha256:734d73c596e73d558145c6d2c2fc646021f48cc7fb94b7446cbc1eeeddb90db9

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
