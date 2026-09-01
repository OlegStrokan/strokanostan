# Gabriela

This is my OWN description. Tech guy explain things to another tech guy. if you want clean ass readme - check README.md file. 

--------------------------------------------
just check order service. it's cool as fuck. like 8/10 on my opinion

## ebay:

### user service

user service probably should be a rest api, but 90% of other microservices use grpc and don't expose any methods for public, all use gateway, so to make it consistent
i prefer consistency to logic. user-service will be grpc microservice, yes it's overkill, but zhizn' igra, igray krasivo

### auth service

some auth stuff. complexity same as user.  simple af. depends on user

### order service:
saga based transaction with sprinkle of outbox transactions with stop/resume saga transaction, fully compensatable steps with escalation to jira or help desk, kafka workers, dlq, partition stopping/resuming, ddd aggregates, distributed locking, watchdogs, event sourcing, snapshots, projection models, cqrs, and ALMOST exactly-once processing. also support b2b orders and subscription (recurring order). supraphysiological amount of lean unit tests, also some integration and e2e tests type shit.

### payment service:
kinda complex. use stripe api, handle dual path finalization: sync (stripe return response), async (webhook push + reconciliation pull for pending/require-shit payments). something like 'effectively-once' semantic: at-least-once delivery + idempotency + db constraints + outbox + reconciliation

### accounting service:
double-entry ledger. the thing that actually knows how much money exists. before this, "did we refund this guy" = read payment rows + saga context + callbacks and pray

### product service: 
internal kafka cqrs service. used by order (kafka)

### product-admin service:
REST APIcko for product-guy

### inventory service:
simple. saga participant. serializable reservation. nothing cool about this service. junior level shit

### catalog service: 
consumes product events from kafka and write it to elasticsarc. not so much

### email-service: 
simple email + idempotency, DLQ, replay worker and shit. so junior+ level

### search service:
search items in 2 ways:
- without AI => plain elasticsearch
- with AI => call pythonAI service, falls back to elastic on timeout/error

### gateway: 
rest api bottle cap. mick swagger, routing, simple

AI: 

### ai-search-service: 
- receive request, start ai pipeline, make 2 parallel calls to ai and elastic, merge and return result
- directly call llm-query-service and embedding-service
- parser for llm: transform user prompt into structured data (what user means)

### vector-indexer-worker:
- consume product events from kafka and upsert into Qdrant 

### embedding-service
- bridge between LLM and eshop microservices (what vector represents of what user means)

## partners (fake external providers):

i don't wanna hit real stripe/dpd/ppl in tests like a clown, so i wrote fakes. no real money, no real parcel, every result is deterministic from the request. you control the saga branch with magic tokens in orderId/idempotency-key or the postal code suffix. fully reproducible, zero flake.

### my-stripe
fake stripe. in-memory. webhook json signed with same hmac scheme as real stripe. idempotency key has "fail" => declined, amount ends "02" => pending, that kind of shit. payment guy eats this.

### my-dpd
fake DPD carrier. the EASY carrier - sync, reliable, answer comes back instantly, cancel always works (idempotent), webhook is hmac signed. boring on purpose

### my-ppl
fake PPL carrier. the ANNOYING one and that's the whole point:
- booking is two-phase => POST, get 202 + referenceId, then POLL until it says accepted (or rejected). adapter sits in a poll loop.
- cancel BLOCKS once parcel is in transit => 409 => compensation raises an intervention ticket but other steps still roll back. one dick carrier doesn't brick the whole rollback.
- webhook auth is NOT hmac, it's a plain X-PPL-Webhook-Secret header. body not signed.
- fires progressive events (in_transit -> out_for_delivery -> delivered). gateway only cares about *.delivered, throws the in-between ones in the trash.

