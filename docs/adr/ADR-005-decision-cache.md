# ADR-005

Decision Cache

Status: Accepted

---

Context

Authorization is high frequency.

Policy evaluation expensive.

---

Decision

Cache authorization decisions.

TTL = 5 seconds.

Version Based Invalidation.

---

Consequences

Positive

Low Latency

High Throughput

Negative

Eventual Consistency Window