# Build native Ebira

Development dependencies: .NET SDK 8, JDK 17, Android SDK platform 35/build-tools 35.0.0, Gradle 8.9 (or the checked-in wrapper). Runtime packages are bundled in the Windows self-contained publish; the Android app has no external runtime libraries. Keep signing keys outside Git.

## Windows

```powershell
dotnet publish native/Ebira.Windows/Ebira.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/windows
```

Copy scripts/Install-Ebira.*, scripts/Uninstall-Ebira.* and the getting-started, baseline and device-validation documents into dist/windows. Include the .NET runtime license and third-party notices. ZIP that directory for the candidate; sign the Windows executable with the owner's legitimate code-signing identity before public distribution. The installer needs no administrator account and does not silently add firewall rules.

## Android

Set JAVA_HOME to JDK 17 and ANDROID_HOME to the SDK location, accept the SDK licenses, then:

```powershell
cd android
.\gradlew.bat assembleDebug lintDebug
```

The debug task creates a conventional development key in the ignored android/.debug directory. The APK is app/build/outputs/apk/debug/app-debug.apk. The release build intentionally has no embedded signing credentials. Supply the owner's signing configuration separately for a public release. The code uses the system UI toolkit and Java 17 language desugaring; minSdk is 29, target/compileSdk 35.

## Checks

Windows layout and network-adapter ordering checks (does not start a host or interact with other desktop applications):

```powershell
dotnet run --project native/Ebira.LayoutChecks/Ebira.LayoutChecks.csproj
```

Transfer checks:

```powershell
dotnet run --project native/Ebira.Checks/Ebira.Checks.csproj
```

This runs the engine and actual native HTTPS API tests with an exact certificate pin, including rejection of the wrong pin. On restricted Windows sandboxes whose user cryptographic profile/Schannel cannot be used, pass `-- --loopback-http` to test the API over an internal loopback-only HTTP listener instead. It prints the TLS skip explicitly. This mode never enables plaintext on a network interface in the product. `--node-tls` optionally substitutes the independent OpenSSL client shipped with Node for the .NET test client; it does not change the native TLS server.

For JVM checks of the exact Android Api.java transport, compile android/transport-checks/ApiCheck.java and Api.java with android.jar plus a test-only org.json JAR. Set EBIRA_TEST_JAVA to java.exe and EBIRA_TEST_CLASSPATH to those compiled classes plus org.json. Run the .NET checks with `-- --loopback-http --android-transport`. They start an OpenSSL TLS bridge in front of the actual engine and verify exact pinning, two-way multi-chunk binary delivery, hashes, resume offsets and revocation. This is transport evidence, not Android OS/MediaStore/Keystore/lifecycle certification and not native Schannel certification.

See docs/DEVICE-VALIDATION.md for the remaining physical tests. The original Node/browser implementation is retained as a legacy PoC; `Start-PoC.cmd` starts it and `node --test test.js` checks it after start.ps1 has generated its test certificate. It is not the native v1 client.
