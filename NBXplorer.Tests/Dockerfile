FROM mcr.microsoft.com/dotnet/sdk:8.0.404-bookworm-slim AS builder
WORKDIR /source
COPY . .
RUN cd NBXplorer.Tests && dotnet build
WORKDIR /source/NBXplorer.Tests
ENTRYPOINT ["./tests-entrypoint.sh"]
