#!/bin/bash

dotnet run --no-launch-profile --no-build -c Release --project "NBXplorer/NBXplorer.csproj" -- $@
