# Asynchronous functions and futures with continuations

A C# project exploring asynchronous HTTP file downloading using **Task Parallel Library (TPL)**, futures, continuations, and event-driven socket programming.

## Overview

The project implements simultaneous HTTP file downloads using low-level asynchronous socket operations (`BeginConnect()` / `EndConnect()`, `BeginSend()` / `EndSend()`, and `BeginReceive()` / `EndReceive()`).

A custom HTTP parser handles response headers and extracts the `Content-Length` field to determine the expected response body size.

## Implementations

### 1. Event-Driven
- Handles socket operations through asynchronous callbacks.
- Implements HTTP response parsing directly within callbacks.

### 2. Tasks and Continuations
- Wraps socket operations in tasks using `TaskCompletionSource`.
- Chains asynchronous operations using `ContinueWith()`.

### 3. Async/Await
- Uses task-based wrappers for socket operations.
- Orchestrates asynchronous execution using `async` / `await`.

## Technologies & Concepts

- **Language:** C#
- **Networking:** TCP Sockets, HTTP
- **Concurrency:** Simultaneous file downloads
- **TPL:** Task, TaskCompletionSource, ContinueWith()
- **Asynchronous Programming:** Callbacks, futures, continuations, async/await
- **HTTP Parsing:** Response headers and Content-Length
