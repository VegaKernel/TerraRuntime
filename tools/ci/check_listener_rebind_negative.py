"""Exercise the old Unix listener drain on an isolated CI checkout, restoring source afterward."""

from pathlib import Path
import subprocess

source = Path("src/TerraRuntime.Application/ListenerManager.cs")
tests = Path("tests/TerraRuntime.Tests/ListenerManagerTests.cs")
original, original_tests = source.read_bytes(), tests.read_bytes()
try:
    marker = b"socket.Shutdown(SocketShutdown.Both);"
    if original.count(marker) != 1:
        raise SystemExit("Listener negative-control marker changed")
    source.write_bytes(original.replace(marker, b"// Negative control: retain the old drain behavior."))
    # Increase exposure to the Unix native accept cancellation race without adding sleeps/retries
    # to successful connections. Any production regression still fails the ordinary 128-transition test.
    tests.write_bytes(original_tests.replace(b"rebind < 32", b"rebind < 512"))
    subprocess.run(["dotnet", "build", str(tests.parent / "TerraRuntime.Tests.csproj"),
                    "-c", "Release", "-warnaserror", "-v", "quiet"], check=True)
    result = subprocess.run(["dotnet", "tests/TerraRuntime.Tests/bin/Release/net11.0/TerraRuntime.Tests.dll",
                             "-class", "TerraRuntime.Tests.ListenerManagerTests", "-noLogo"],
                            capture_output=True, text=True, timeout=120)
    print(result.stdout)
    print(result.stderr)
    if result.returncode == 0:
        raise SystemExit("Old listener drain did not reproduce; do not claim negative-control evidence")
    if "Same_port_bind_address_change_preserves_existing_client" not in result.stdout + result.stderr:
        raise SystemExit("Negative control failed outside the intended rebind regression")
    print("Old listener drain reproduced the same-port rebind regression")
finally:
    source.write_bytes(original)
    tests.write_bytes(original_tests)
