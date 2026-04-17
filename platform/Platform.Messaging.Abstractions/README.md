# Platform.Messaging.Abstractions

Standardized message header key constants for reliable messaging.

## The Problem

When multiple packages handle message headers (outbox, inbox, producers, consumers), inconsistent header key strings lead to:

- Typos breaking idempotency (`"message.id"` vs `"messageId"`)
- Different teams using different conventions
- No compile-time safety for header names

## The Solution

A single source of truth for header key constants. All messaging packages reference these constants, ensuring consistency across the entire messaging pipeline.

## Contents

- `MessageHeaderKeys` - Standard header key constants

## MessageHeaderKeys

| Constant | Value | Purpose                                                                                                      |
|----------|-------|--------------------------------------------------------------------------------------------------------------|
| `MessageId` | `"message.id"` | Unique identifier (GUID v7) for idempotent processing - consumers can use this to detect and skip duplicates |
| `Origin` | `"origin"` | Service identifier that produced the message - useful for debugging and tracing message flow                 |

## Usage

```csharp
using Platform.Messaging.Abstractions;

// Setting headers
headers[MessageHeaderKeys.MessageId] = Guid.CreateVersion7().ToString();
headers[MessageHeaderKeys.Origin] = "OrderService";

// Reading headers
var messageId = headers[MessageHeaderKeys.MessageId];
```

## Related Packages

- [Platform.ReliableMessaging.Outbox.EFCore](../Platform.ReliableMessaging.Outbox.EFCore) - Uses these headers when adding outbox messages
- [Platform.KafkaFlow.Inbox.EFCore](../Platform.KafkaFlow.Inbox.EFCore) - Reads MessageId for deduplication
