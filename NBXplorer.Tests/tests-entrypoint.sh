#!/bin/sh
set -e

dotnet test --filter "Benchmark!=Benchmark&Maintenance!=Maintenance" --no-build -v n --logger "console;verbosity=normal" < /dev/null
