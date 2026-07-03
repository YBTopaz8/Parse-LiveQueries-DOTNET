## v3.8.0 **WebAssembly (WASM) Stability, Compile-Time Null-Safety, and Fluent MongoDB Aggregations**

This release focuses heavily on compiler-level safety, query modernization, and deep dependency isolation. I have implemented full support for Nullable Reference Types, introduced a fluent, LINQ-style MongoDB Aggregation pipeline, and significantly expanded the internal test suite to over 340+ passing tests, ensuring enterprise-grade stability across multi-threaded and WASM environments.

---

### 🎨 Key Features & Enhancements

#### 1. Strongly-Typed Query Expressions (The "Magic-String Killer")
You can now build queries and pipelines using native C# lambda expressions instead of hardcoded strings. The new `ExpressionHelper` recursively parses deep property paths (handling both reference types and value types) and safely maps them to your `[ParseFieldName]` attributes at every level.

*   **How it works under the hood:** If a property has a `[ParseFieldName("user_profile")]` attribute, the helper will strip away the C# property name and translate `x => x.Profile.Profile.Views` directly to the database column path `"user_profile.user_profile.views_count"`.

```csharp
// Previously (Fragile runtime string queries):
var query = new ParseQuery<Customer>(client).WhereEqualTo("customer_city", "Paris");

// New in 3.8.0 (Robust compile-time verified queries):
var query = new ParseQuery<Customer>(client).WhereEqualTo(x => x.City, "Paris");
```

---

#### 2. Fluent MongoDB Aggregation Pipelines
I have introduced `ParsePipelineBuilder<T>`, providing a clean, strongly-typed interface to execute advanced Server-Side operations like grouping, accumulators (`Sum`, `Avg`, `Max`, `Min`, `Push`, `AddToSet`), sorting, and pagination directly against the Parse REST API.

*   **Example (E-Commerce Sales Dashboard):**
    ```csharp
    var report = await new ParseQuery<Order>(client)
        .Aggregate()
        .Match(new ParseQuery<Order>(client).WhereEqualTo(x => x.Status, "completed"))
        .Group(x => x.SellerId, 
            ("TotalRevenue", ParseAccumulator.Sum, "price"),
            ("AverageOrder", ParseAccumulator.Avg, "price"),
            ("TotalCount", ParseAccumulator.Sum, "1") // Passing "1" counts rows in MongoDB
        )
        .Sort("TotalRevenue", descending: true)
        .Limit(5)
        .ExecuteAsync();
    ```

*   **Expected Result Structure:**
    Instead of instantiating heavy, tracking-enabled `ParseObject` classes, aggregations execute on the server and return a lightweight list of raw dictionaries (`IEnumerable<IDictionary<string, object>>`), where each dictionary represents an aggregated bucket:
    ```csharp
    // The resulting list contains items structured like this:
    // Row 1:
    // { "_id": "seller_abc", "TotalRevenue": 15450.50, "AverageOrder": 150.25, "TotalCount": 103 }
    // Row 2:
    // { "_id": "seller_xyz", "TotalRevenue": 8900.00,  "AverageOrder": 89.00,  "TotalCount": 100 }
    ```

---

#### 3. Relational `FetchWithIncludeAsync` (Single-Object Pointer Expansion)
In standard Parse development, calling `await object.FetchAsync()` only populates the local fields of that object, leaving relational pointers "hollow" (data unavailable). To expand those pointers, developers historically had to write tedious, verbose query logic. 

With `FetchWithIncludeAsync`, you can now fetch a `ParseObject` and recursively expand its relational Pointers in a single network round-trip.

*   **Example:**
    ```csharp
    // Create a hollow order pointer
    var order = ParseObject.CreateWithoutData<Order>("order123");

    // Fetch the order data AND recursively expand the 'customer' pointer 
    // and the customer's 'address' pointer in ONE network call:
    await order.FetchWithIncludeAsync("customer", "customer.address");

    // You can now access nested properties synchronously without network hits:
    var customer = order.Get<ParseUser>("customer");
    var address = customer.Get<ParseObject>("address");
    var city = address.Get<string>("city");
    ```

---

#### 4. Distinct Queries & Full-Text Search (FTS)
*   **Distinct Query support:** Allows you to retrieve all unique values for a specific key matching your query criteria in a single database step.
    ```csharp
    // Retrieve all unique zip codes where customers are located:
    IEnumerable<string> zipCodes = await new ParseQuery<Customer>(client)
        .WhereEqualTo(x => x.IsActive, true)
        .DistinctAsync<string>("zipCode");
    ```
*   **Full-Text Search (FTS):** Added `WhereFullTextMatches` to search using text indexes on the database server.
    ```csharp
    var query = new ParseQuery<Article>(client).WhereFullTextMatches("content", "parse sdk .net");
    ```

---

### 🛡️ Architecture & Stability

*   **Comprehensive Nullable Reference Types (`#nullable enable`):**
    Part of the core library has been audited and migrated to support C# Nullable Reference Types. Method signatures, asynchronous tasks, and core models (`ParseObject`, `ParseQuery`) now accurately reflect null-states, helping you catch `NullReferenceExceptions` at compile-time instead of runtime.

*   **Batch Save Optimization (`SaveAllAsync`):**
    The saving pipeline has been refactored under the hood. It now natively packages parallel saves into a single `/batch` HTTP request (maximum of 50 objects per batch). This eliminates TCP socket exhaustion on mobile platforms, bypasses parallel rate limits, and simplifies the return type to a clean `Task<IEnumerable<IObjectState>>` by removing old nested task unwrapping.

*   **LiveQuery State Isolation:**
    Decoupled `ParseLiveQueryClient` from the global static `ParseClient.Instance`. Service Hubs are now strictly scoped to the client instance, guaranteeing absolute thread-safety during parallel Unit Tests or multi-tenant server environments.
    *   *The Unsubscribe Bug Fix:* This separation resolved a critical race condition where client-side subscription cleanup occurred prior to server confirmation, which previously suppressed the execution of local `DidUnsubscribe` events.

---

### 🧪 Testing & Verification

*   **340+ Verified Tests:** The internal test suite has been drastically expanded. It now features full `LiveQuery` integration scenarios (covering Social/Chat apps, dropped-packet reconnections, and casting safety) and rigorous deterministic queueing verification.

*(Note for developers upgrading from 3.7.x: All new Expression features are added as non-breaking overloads. Your existing string-based queries will continue to compile and function perfectly.)*