
# v3.4.0: Enhanced Subscription Management and Refined Error Handling ✨

This release focuses on improving the subscription process and making error handling more informative and consistent. We've refined internal workings to provide a smoother, more predictable LiveQuery experience. **This version builds cleanly with no warnings, ensuring a smoother development experience. - Finally! 🫡**

**Key Improvements:**

*   **Streamlined Subscription Creation:** The `ISubscriptionFactory` has been updated. The `CreateSubscription` method now includes an `unsubscribeAction` parameter. This allows for more robust and reliable cleanup when subscriptions are no longer needed.  This is a *breaking change* for anyone with a custom `ISubscriptionFactory` implementation.
    ```csharp
    // Updated interface:
    Subscription<T> CreateSubscription<T>(int requestId, ParseQuery<T> query, Action<Subscription> unsubscribeAction) where T : ParseObject;
    ```

*   **Simplified Unsubscription:**  The `Subscribe` method within `ParseLiveQueryClient` now expertly handles the creation of the `unsubscribeAction`. This simplifies the subscription process and guarantees correct resource management.  Added `SubscriptionExtensions` providing utility methods for easy unsubscription: `UnsubscribeNow()` and `UnsubscribeAfter(long timeInMinutes)`.

*   **More Informative Error Reporting:**
    *   Improved error handling in `ParseObject` and `ParseUser` now includes detailed exception messages, making it easier to diagnose issues.
    *   Error logging in `WebSocketClient` during connection closure has been enhanced, providing better insights into connection problems.
    * The `DidEncounter` method within `Subscription<T>` benefits from improved error handling, passing the error to the subscription to provide error to users.

*   **Internal Enhancements:**
    *   `TaskQueue` has been refined:
        *   `TaskQueue.Tail` is now guaranteed to be non-nullable.
        *   `TaskQueue.Enqueue` now returns a non-nullable `Task<ParseUser>`.
    *   `ParseCurrentUserController.currentUser` is now non-nullable, reflecting its expected state more accurately.

**Breaking Changes:**

*   **`ISubscriptionFactory` Interface:** As mentioned above, the `CreateSubscription` method signature has changed. Custom implementations *must* be updated to include the `Action<Subscription> unsubscribeAction` parameter.

**Upgrade Notes:**

*   If you have implemented a custom `ISubscriptionFactory`, you *must* update its `CreateSubscription` method to match the new signature.
*   While this release primarily focuses on internal improvements, thorough testing of your application after upgrading is always recommended.
*   Enjoy a more streamlined and informative LiveQuery experience!

**Happy Coding! 👍**


