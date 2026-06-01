
# v3.6.1: Fixed OnUpdate() was returning null for new object
## What's Changed
- Fixed an issue where OnUpdate would only push the orginal values while updated values where null

# v3.6.0: Added WASM Support, Refined Subscription Management ✨

## What's Changed
- Added Support for WASM Projects by skipping attempts to keep the websocket alive in browsers.

- Fixed Support for Subscription.Event.Update in the .On() extension method.
It was not handled previously at all

- Using Subscription.Event.Update) in .On() will ensure you are provided the Updated parse object,

If you need to have the original and the updated parse object, you can still subscribe to OnUpdate() like this
``` 
liveQuerySubscription.OnUpdate((originalObj,UpdatedObj) =>
{
// logic goes here
});
```

- Did some better renaming to objects/variables

