FROM microsoft/dotnet:2.1.402-sdk-alpine3.7 AS builder
WORKDIR /source
COPY NBXplorer/NBXplorer.csproj NBXplorer/NBXplorer.csproj
# Cache some dependencies
RUN cd NBXplorer && dotnet restore && cd ..
COPY . .
RUN cd NBXplorer && \
    dotnet publish --output /app/ --configuration Release

FROM microsoft/dotnet:2.1.4-aspnetcore-runtime-alpine3.7
WORKDIR /app

RUN mkdir /datadir
ENV NBXPLORER_DATADIR=/datadir
VOLUME /datadir

EXPOSE 32838
ENV NBXPLORER_NETWORK=""
ENV NBXPLORER_BIND=""
ENV NBXPLORER_NOAUTH=""
ENV NBXPLORER_BTCRPCURL=""
ENV NBXPLORER_BTCRPCUSER=""
ENV NBXPLORER_BTCRPCPASSWORD=""
ENV NBXPLORER_BTCNODEENDPOINT=""
ENV NBXPLORER_BTCSTARTHEIGHT=""
ENV NBXPLORER_BTCRESCAN=""
ENV NBXPLORER_AWSSQSBLOCKQUEUEURL=""
ENV NBXPLORER_AWSSQSTRANSACTIONQUEUEURL=""


COPY --from=builder "/app" .
ENTRYPOINT ["dotnet", "NBXplorer.dll"]