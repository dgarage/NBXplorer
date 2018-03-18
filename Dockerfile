FROM microsoft/aspnetcore-build:2.0.6-2.1.101-stretch AS builder
WORKDIR /source
COPY NBXplorer/NBXplorer.csproj NBXplorer/NBXplorer.csproj
# Cache some dependencies
RUN cd NBXplorer && dotnet restore && cd ..
COPY . .
RUN cd NBXplorer && dotnet publish --output /app/ --configuration Release

FROM microsoft/aspnetcore:2.0.6-stretch
WORKDIR /app

RUN mkdir /datadir
ENV NBXPLORER_DATADIR=/datadir
VOLUME /datadir

COPY --from=builder "/app" .
ENTRYPOINT ["dotnet", "NBXplorer.dll"]