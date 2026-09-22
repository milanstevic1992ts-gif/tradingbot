#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
PROJECT="$ROOT/GE360.Trading.AlpacaPluginHost/GE360.Trading.AlpacaPluginHost.csproj"
OUTPUT="$ROOT/GE360.Trading.AlpacaPluginHost/bin/$CONFIGURATION/net10.0"
PLUGIN_DIR="${GE360_ALPACA_PLUGIN_DIR:-$ROOT/ge360-plugins/alpaca}"

dotnet build "$PROJECT" --configuration "$CONFIGURATION"

rm -rf "$PLUGIN_DIR"
mkdir -p "$PLUGIN_DIR"

copied=0
for file in "$OUTPUT"/*.dll; do
  [ -e "$file" ] || continue
  name="$(basename "$file")"

  case "$name" in
    QuantConnect.Brokerages.Alpaca.dll|\
    Alpaca.Markets.dll|\
    MessagePack.dll|\
    MessagePack.Annotations.dll|\
    Microsoft.NET.StringTools.dll|\
    Polly.dll|\
    Polly.Core.dll|\
    Newtonsoft.Json.dll|\
    System.IO.Pipelines.dll|\
    System.Threading.Channels.dll)
      cp "$file" "$PLUGIN_DIR/$name"
      copied=$((copied + 1))
      ;;
  esac
done

test -f "$PLUGIN_DIR/QuantConnect.Brokerages.Alpaca.dll"
test -f "$PLUGIN_DIR/Alpaca.Markets.dll"

echo "GE360 Alpaca plugin staged: $PLUGIN_DIR ($copied assemblies)"
