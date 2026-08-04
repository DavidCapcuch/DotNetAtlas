FROM mcr.microsoft.com/devcontainers/dotnet:10.0-noble@sha256:0ad11caaa728e35cbc46b0592dd11ca0df8111e565a96471024a795f4d412c7c

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
