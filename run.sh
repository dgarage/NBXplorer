#!/bin/bash

dotnet run --no-launch-profile --no-build -c Release -p "NBXplorer/NBXplorer.csproj" -- $@
