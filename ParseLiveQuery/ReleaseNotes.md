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