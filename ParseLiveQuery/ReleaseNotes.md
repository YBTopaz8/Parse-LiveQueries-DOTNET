# v3.2.0: Robust LiveQuery Client with Enhanced Reliability and Stability 🚀
**Key Enhancements:**

*   **Automatic Reconnection:** Implemented automatic reconnection to the LiveQuery server upon connection loss.
    *   Features exponential backoff with configurable retries.
    *   Reports reconnection attempts and failures via the `OnError` observable.

*   **Heartbeat (Ping/Pong):** Integrated WebSocket Ping/Pong frames for robust connection health monitoring.
    *   Detects and recovers from "dead" connections, enhancing stability.

*   **Configurable Receive Buffer:** Added ability to customize the WebSocket receive buffer size.
    *   Useful for handling large messages.
    *   Utilize: `new WebSocketClient(uri, callback, bufferSize);`

*   **Refactored WebSocket Handling:**
    *   Complete rewrite for improved stability, error handling, and performance.
    *   Ensures proper resource disposal (`Dispose()`) to prevent memory leaks.
    *   Provides clear and consistent WebSocket connection state management.
    *   Includes placeholder for binary message handling.
    *   Allows control over WebSocket close status and description.

*   **Optimized Subscription Management:**
    *   Simplified unsubscription process.
    *   Ensures thread-safe subscription operations.
    *   Removed reflection for enhanced performance.

*   **Full `IDisposable` Implementation:** `ParseLiveQueryClient`, `WebSocketClient`, and `Subscription` classes now fully implement `IDisposable`.
    *   Guarantees proper resource cleanup.

*   **Enhanced Error Handling:** Improved error handling with more informative exceptions and events.

*   **Optimized JSON Handling:** Enhanced JSON serialization/deserialization.

**Breaking Changes:**

*   None.  This release maintains backward compatibility with v3.1.0. Thorough testing is still strongly recommended.

**Upgrade Notes:**

*   Thorough application testing is highly recommended after upgrading due to significant internal improvements.
*   New features (reconnection, buffer size) are optional and do not require code changes to maintain existing functionality.

**Happy Coding! 👋🏾**


# v3.0.1 - QOL update 🎇
## It's no longer REQUIRED to be logged in order to connect to LQ.  
Any client device can connect to Live Queries independently

- Fixed #8  (saves dates as string for now instead of just not saving)
- Fixed #4  
- Closes #7 too 


# v3.0.0 🎄

##### Major Update! - VERY IMPORTANT FOR SECURITY

Fixed an issues where connected Live Queries would never actually close stream with server (causing memory leaks)
This eliminates any instance where a "disconnected" user would still be able to receive data from the server and vice versa.
Please update to this version as soon as possible to avoid any security issues.
The best I would suggest is to migrate the currently observed classes to a new class and delete the old one.
You can easily do this by creating a new class and copying the data from the old class to the new one (via ParseCloud in your web interface like in Back4App).

More Fixes...
- Fixed issues where `GetCurrentUIser()` from Parse did **NOT** return `userName` too.
- Fixed issues with `Relations/Pointers` being broken.

Thank You!

## v2.0.4
Improvements on base Parse SDK.
- LogOut now works perfectly fine and doesn't crash app!
- SignUpWithAsync() will now return the Signed up user's info to avoid Over querying.
- Renamed some methods.
- Fixed perhaps ALL previous UNITY crashes.

(Will do previous versions later - I might never even do it )