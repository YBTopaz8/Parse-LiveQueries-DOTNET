***

# 🚀 Release v3.8.1: Enhanced DX & Strongly-Typed Relations

This release focuses heavily on **Developer Experience (DX)**. We are eliminating "magic strings" in queries and removing the frustration of cryptic SDK errors when dealing with ParseObject subclasses. 

### ✨ What's New

#### 1. Strongly-Typed `WhereEqualTo` for Relations
Querying against `ParseRelation` properties previously required hard-coded strings, which are prone to typos and break during code refactoring. We have introduced a new strongly-typed overload for `WhereEqualTo` that accepts property expressions!

**Before (v3.7.x - Magic Strings):**
```csharp
var roles = await ParseClient.Instance.GetQuery<ParseRole>()
    .WhereEqualTo("users", currentUser) // Prone to typos!
    .FindAsync();
```

**Now (v3.8.1 - Strongly Typed):**
```csharp
var roles = await ParseClient.Instance.GetQuery<ParseRole>()
    .WhereEqualTo(x => x.Users, currentUser) // Compile-time safe!
    .FindAsync();
```
*Note: This recursively resolves `[ParseFieldName]` attributes, ensuring your queries perfectly map to your MongoDB collections.*

#### 2. "Quality of Life" Subclass Registration Errors
Forgetting to register a custom `ParseObject` subclass at startup is the #1 most common mistake when setting up a Parse project. Previously, this resulted in vague `NullReferenceException` or standard casting errors deep in the SDK. 

In `v3.8.1`, the `GenerateObjectFromState` pipeline has been overhauled with Nullable support (`T?`). If the SDK detects an unregistered subclass, it intercepts the failure and throws a highly detailed, actionable `InvalidCastException`.

**The New Error Output:**
```text
[Parse SDK Error] Failed to cast the server response to 'GameScore'.
Reason: The class 'GameScore' has not been registered with the SDK.
Fix: Add 'ParseClient.Instance.RegisterSubclass(typeof(GameScore));' to your application startup code BEFORE executing queries.
```
No more guessing why your queries are failing to map!

#### 3. General Cleanups
* **Nullable Instantiations:** Refactored `IParseObjectClassController` and `ObjectServiceExtensions` to fully utilize nullable reference types (`ParseObject?` and `T?`) for instantiation methods.
* **Internal Signatures:** Updated internal method signatures and added minor code cleanups for clarity, safety, and consistency across the `ParseObject` lifecycle.

---

### 💡 Developer Tip for v3.8.1

If you are building complex aggregation pipelines using the new features from v3.8.0, you can now seamlessly combine them with the new strongly-typed relations:

```csharp
// Example: Safely find the names of all roles a user belongs to
var roleNames = await ParseClient.Instance.GetQuery<ParseRole>()
    .WhereEqualTo(x => x.Users, user)     // Strongly-typed relation check
    .Aggregate()                          // Open the pipeline
    .Project(x => x.Name)                 // Project strongly-typed fields
    .ExecuteAsync();
```

--- 
**Update today via NuGet:**
`dotnet add package YB.ParseLiveQueryDotNet --version 3.8.1`