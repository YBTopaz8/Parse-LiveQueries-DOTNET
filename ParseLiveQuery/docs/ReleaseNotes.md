
# v3.7.1 Key Highlights & Enhancements
1. WebAssembly (WASM) & Browser Compatibility (CORS Fixes)
WASM Preflight Header Filtering: Prevented the SDK from automatically appending custom metadata headers (`X-Parse-App-Build-Version`, `X-Parse-App-Display-Version`, and `X-Parse-OS-Version`) when running in a browser environment. Added compile-time preprocessor checks and a native .NET runtime OperatingSystem.IsBrowser() check. This resolves the browser-level CORS blocking error (`TypeError: NetworkError`).

Modernized Client Key Header Routing: Replaced the legacy `X-Parse-Windows-Key` (a fossil from Windows Phone 8 days) with the standard, CORS-approved `X-Parse-Client-Key` header when compiling/running in a WebAssembly context.

    Note for WASM devs: When initializing ParseClient in Wasm, simply supply your standard Parse Client Key (iOS/Android key) instead of the legacy .NET Key.

2. Type System & Serialization Upgrades

    - Native C# Enum Support: Upgraded the core `Conversion.cs` deserialization engine to natively support C# Enums. The SDK now automatically converts database strings (e.g., "Web") or integers (e.g., 3) back into their strongly-typed C# Enums on read. 
    Developers no longer need to write custom getters/setters or `.ToString()` wrappers in their Parse models.

    - Native DateTimeOffset Support: Added native validation and serialization support for DateTimeOffset inside `ParseDataEncoder.cs`. 
    The SDK now automatically converts `DateTimeOffset` to UTC DateTime and formats it as a standard Parse ISO 8601 Date object ( { "__type": "Date", "iso": "..." }) before sending it to the Parse Server, eliminating timezone-shifting bugs.

3. Under-the-Hood Cleanup

    StreamReader Double-Dispose Protection: Patched internal stream reading processes to ensure that stream readers use the `leaveOpen: true` flag. This prevents unmanaged underlying network and IO streams from being closed prematurely during cancellation, resolving a silent handle-corruption bug on physical platform channels.