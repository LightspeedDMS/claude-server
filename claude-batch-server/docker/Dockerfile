FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Install system dependencies
RUN apt-get update && apt-get install -y \
    sudo \
    rsync \
    btrfs-progs \
    curl \
    wget \
    && rm -rf /var/lib/apt/lists/*

# Install Claude Code CLI
RUN curl -fsSL https://claude.ai/install.sh | bash
ENV PATH="/root/.local/bin:$PATH"

# Create workspace directories
RUN mkdir -p /workspace/repos /workspace/jobs /var/log/claude-batch-server

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj", "src/ClaudeBatchServer.Api/"]
COPY ["src/ClaudeBatchServer.Core/ClaudeBatchServer.Core.csproj", "src/ClaudeBatchServer.Core/"]

# Restore dependencies
RUN dotnet restore "src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj"

# Copy source code
COPY . .

# Build application
WORKDIR "/src/src/ClaudeBatchServer.Api"
RUN dotnet build "ClaudeBatchServer.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ClaudeBatchServer.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set up proper permissions for the app directory
RUN chmod -R 755 /app

# Create a non-root user for better security (service will still run as root for user impersonation)
RUN useradd -m -s /bin/bash claude-batch && \
    usermod -aG sudo claude-batch

ENTRYPOINT ["dotnet", "ClaudeBatchServer.Api.dll"]