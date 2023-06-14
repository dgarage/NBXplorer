FROM mcr.microsoft.com/dotnet/sdk:6.0.401-bullseye-slim AS builder
ARG SupportDBTrie=true
ENV SupportDBTrie $SupportDBTrie
WORKDIR /source
COPY . .
RUN cd NBXplorer.Tests && dotnet build
WORKDIR /source/NBXplorer.Tests
ENTRYPOINT ["./tests-entrypoint.sh"]
