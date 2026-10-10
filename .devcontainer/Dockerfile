FROM mcr.microsoft.com/devcontainers/dotnet:10.0-noble@sha256:694076b55f447d4b9f5b49bc6616c2e904d63e1a61e43680b2bbea46011b6783

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
