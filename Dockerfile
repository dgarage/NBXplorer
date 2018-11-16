FROM microsoft/dotnet:2.1.500-sdk-alpine3.7 AS builder
WORKDIR /source
COPY NBXplorer/NBXplorer.csproj NBXplorer/NBXplorer.csproj
# Cache some dependencies
RUN cd NBXplorer && dotnet restore && cd ..
COPY . .
RUN cd NBXplorer && \
    dotnet publish --output /app/ --configuration Release

FROM microsoft/dotnet:2.1.6-aspnetcore-runtime-alpine3.7
WORKDIR /app

RUN mkdir /datadir
ENV NBXPLORER_DATADIR=/datadir
VOLUME /datadir

COPY --from=builder "/app" .
ENTRYPOINT ["dotnet", "NBXplorer.dll"]