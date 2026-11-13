FROM mcr.microsoft.com/devcontainers/dotnet:10.0-noble@sha256:86b40dd4bb6d4b58e9cee1f997203ca084e8447aa5a29df50c70c7bf9f1ffcc9

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
