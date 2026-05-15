.PHONY: sony android wasm api all clean seed

# ── Android / device ─────────────────────────────────────────────────────────
#
# Debug builds use Fast Deployment: dotnet run pushes the APK *and* the managed
# assemblies to the device in one step.  Plain `adb install` only pushes the
# APK and leaves the assemblies missing, causing a native crash on startup.
#
# The Sony Xperia XQ-BC52 is auto-detected by the .NET Android tooling as long
# as it is connected over ADB (USB or wireless).

sony:
	dotnet run --project VirtualLibrary.Client \
	    -f net10.0-android \
	    -p:AllTargets=true \
	    -p:RuntimeIdentifier=android-arm64

# Build the Android APK without deploying (CI / release prep).
android:
	dotnet build VirtualLibrary.Client \
	    -f net10.0-android \
	    -p:AllTargets=true \
	    -p:RuntimeIdentifier=android-arm64

# ── WebAssembly ───────────────────────────────────────────────────────────────

wasm:
	dotnet run --project VirtualLibrary.Client -f net10.0-browserwasm

# ── API (ASP.NET Core backend) ────────────────────────────────────────────────

api:
	dotnet run --project VirtualLibrary.Api

# ── Convenience ──────────────────────────────────────────────────────────────

all: android wasm

clean:
	dotnet clean VirtualLibrary.Client
	dotnet clean VirtualLibrary.Api

# ── Discovery seeding ─────────────────────────────────────────────────────────

seed:
	python3 scripts/seed_discovery.py
