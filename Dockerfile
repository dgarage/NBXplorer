FROM microsoft/dotnet:2.1.300-rc1-sdk-alpine3.7 AS builder
WORKDIR /source
COPY NBXplorer/NBXplorer.csproj NBXplorer/NBXplorer.csproj
# Cache some dependencies
RUN cd NBXplorer && dotnet restore && cd ..
COPY . .
RUN cd NBXplorer && \
    dotnet add package ILLink.Tasks --version 0.1.5-preview-1461378 --source https://dotnet.myget.org/F/dotnet-core/api/v3/index.json && \
    dotnet publish --output /app/ --configuration Release -r linux-musl-x64

FROM microsoft/dotnet:2.1.0-rc1-runtime-deps-alpine3.7
WORKDIR /app

RUN mkdir /datadir
ENV NBXPLORER_DATADIR=/datadir
VOLUME /datadir

COPY --from=builder "/app" .
ENTRYPOINT ["./NBXplorer"]