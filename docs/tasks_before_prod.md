# qdrant - Create collection (one-time setup or on first startup)
curl -X PUT http://localhost:6333/collections/products \
  -H "Content-Type: application/json" \
  -d '{
    "vectors": { "size": 768, "distance": "Cosine" }
  }'


  # pg: Proper Migration Strategy (for production)
For production, you should replace EnsureCreatedAsync() with EF Core Migrations:

  # For each service, generate initial migration:
cd Product/Product/Api
dotnet ef migrations add InitialCreate --project ../Infrastructure --startup-project .

cd Order/Order/Api
dotnet ef migrations add InitialCreate --project ../Infrastructure --startup-project .

cd Payment/Payment/Api
dotnet ef migrations add InitialCreate --project ../Infrastructure --startup-project .





add gRPC resilience policies: retry, timeout, or circuit breaker on any gRPC client.