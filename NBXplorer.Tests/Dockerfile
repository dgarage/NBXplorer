FROM mcr.microsoft.com/dotnet/sdk:10.0.202-noble AS builder
WORKDIR /source
COPY . .
RUN cd NBXplorer.Tests && dotnet build
WORKDIR /source/NBXplorer.Tests
ENTRYPOINT ["./tests-entrypoint.sh"]
