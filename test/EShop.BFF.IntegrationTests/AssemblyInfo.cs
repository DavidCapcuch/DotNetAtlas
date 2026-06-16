// This project now hosts several Testcontainer-backed fixtures (redis-cache for product-page, home-page,
// and the warm fixtures; redis-cache + Kafka + Schema Registry for cache invalidation). Running their
// collections in parallel boots multiple containers at once, and concurrent Docker.DotNet calls over the
// Windows named pipe interleave on the shared ChunkedReadStream ("Invalid chunk header"). Serialize the
// collections so the fixtures boot one at a time — deterministic over fast-but-flaky.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
