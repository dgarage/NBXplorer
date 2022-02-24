FROM mcr.microsoft.com/dotnet/sdk:6.0.101-bullseye-slim AS builder

WORKDIR /source
COPY . .
RUN cd NBXplorer.Tests && dotnet build
WORKDIR /source/NBXplorer.Tests
ENTRYPOINT ["./tests-entrypoint.sh"]
