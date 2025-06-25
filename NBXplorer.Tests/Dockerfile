FROM mcr.microsoft.com/dotnet/sdk:10.0.201-noble AS builder
WORKDIR /source/NBXplorer
COPY . .
WORKDIR /source
# TODO: Replace with NuGet package reference once NBitcoin package is published with Decred support.
RUN git clone --depth 1 --branch adddecred https://github.com/joegruffins/NBitcoin.git
WORKDIR /source/NBXplorer
RUN cd NBXplorer.Tests && dotnet build
WORKDIR /source/NBXplorer/NBXplorer.Tests
ENTRYPOINT ["./tests-entrypoint.sh"]
