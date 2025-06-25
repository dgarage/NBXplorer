FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.201-noble AS builder
WORKDIR /source/NBXplorer
COPY NBXplorer/NBXplorer.csproj NBXplorer/NBXplorer.csproj
COPY NBXplorer.Client/NBXplorer.Client.csproj NBXplorer.Client/NBXplorer.Client.csproj
WORKDIR /source
# TODO: Replace with NuGet package reference once NBitcoin package is published with Decred support.
RUN git clone --depth 1 --branch adddecred https://github.com/joegruffins/NBitcoin.git
# Cache some dependencies
WORKDIR /source/NBXplorer
RUN cd NBXplorer && dotnet restore && cd ..
COPY . .
RUN cd NBXplorer && \
    dotnet publish --output /app/ --configuration Release

FROM mcr.microsoft.com/dotnet/aspnet:10.0.5-noble
WORKDIR /app

RUN mkdir /datadir
ENV NBXPLORER_DATADIR=/datadir
VOLUME /datadir

COPY --from=builder "/app" .
ENTRYPOINT ["dotnet", "NBXplorer.dll"]
