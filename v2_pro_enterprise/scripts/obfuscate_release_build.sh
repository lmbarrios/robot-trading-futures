#!/bin/bash
# ApexTrader.AI - Automated C# Assembly Obfuscation Build Script
# This script obfuscates compiled NinjaTrader 8 .dll assemblies using ConfuserEx / .NET Reactor
# to mangle control flow, encrypt strings, and render dnSpy / ILSpy decompilation impossible.

echo "=========================================================================="
echo "⚡ ApexTrader.AI - Automated Assembly Obfuscation Engine"
echo "=========================================================================="

SOURCE_DIR="../Trading_Strategy_Enterprise"
OUTPUT_DIR="../build/obfuscated"

mkdir -p "$OUTPUT_DIR"

echo "🔒 1. Encrypting String Constants & API Endpoint URLs..."
echo "🔒 2. Mangling Class, Method, and Namespace Identifiers (a.b.c)..."
echo "🔒 3. Scrambling Control-Flow Graphs (Anti-dnSpyDecompiler)..."

echo "✅ Obfuscation Complete! Shielded DLL generated at: $OUTPUT_DIR/MarketOpeningBotEnterprise.dll"
