
# v3.7.0 Key Highlights & Enhancements
1. Deep Serialization & Recursive Encoding Fixes (Core)

Recursive Set-Operation Serialization: Fixed a silent bug in `ParseSetOperation` and `ParseDataEncoder` where complex objects (like ParseACL or nested Pointers) assigned via standard C# properties were serialized as raw C# objects rather than JSON-compliant dictionaries.

Property-Level ACL & Pointer Security: Developers can now use standard, strongly-typed C# property setters (e.g., `object.ACL = myAcl` ) instead of dictionary-based `.Set()` fallbacks. The SDK now safely recursively converts and serializes these properties, preventing unexpected "Master Key Only" saves on the server.

Optimized Array Operations: Streamlined `ParseAddOperation` and `ParseAddUniqueOperation` to leverage the centralized `PointerOrLocalIdEncoder`, eliminating duplicate serialization logic and improving type support.

2. LiveQuery Resiliency & Cast Protections (Rx)

Automatic Subclass Fallbacks: Fixed an `InvalidCastException` inside LiveQuery subscriptions when processing incoming events for custom subclasses. If a developer forgets to register a subclass globally, the Subscription<T> will now gracefully construct the type T using the received object state rather than crashing.

Automatic Network State Recovery: Simplified WebSocket subscription lifetimes during network drops. The LiveQuery client now manages background reconnects natively without requiring developers to manually tear down and re-attach UI event listeners upon internet restoration.