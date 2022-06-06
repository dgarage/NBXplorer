#!/bin/sh
set -e

dotnet test --filter "Azure!=Azure&Broker!=RabbitMq&Benchmark!=Benchmark&Maintenance!=Maintenance" --no-build -v n --logger "console;verbosity=normal" < /dev/null
