#!/bin/sh
set -e

dotnet test --filter "Azure!=Azure&Broker!=RabbitMq&Benchmark!=Benchmark" --no-build -v n --logger "console;verbosity=normal" < /dev/null
