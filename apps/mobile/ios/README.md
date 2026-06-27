# OneDesk iOS Shell

The iOS shell is part of the current OneDesk product scope. Routine validation currently targets Android only, so this folder contains the Swift/WKWebView shell source skeleton and integration notes.

## Runtime Rules

- Load the mobile Vue frontend from app-bundled files.
- Block remote navigation and remote resource requests where WKWebView allows it.
- Expose JSAPI through `WKScriptMessageHandler`.
- Return `CapabilityNotSupported` for unsupported iOS capabilities.
- Route non-local JSAPI calls through the connected desktop gateway.
