# API Testing Guide — strokanostan (free-ebay)

**Base URL**: `http://localhost:5081` (Gateway)  
**ProductAdmin URL**: `http://localhost:5050` (direct, no Gateway proxy)  
**Auth Header**: `Authorization: Bearer <jwt_token>`  
**Admin API Key Header**: `X-Admin-Api-Key: <key>`

---

## All Endpoints (71 total)

### Auth (9 endpoints — no auth required)

```bash
# 1. Register
POST /api/v1/auth/register
{
  "email": "buyer@test.com",
  "password": "StrongP@ss123",
  "fullName": "Test Buyer",
  "phone": "+1234567890"
}

# 2. Login
POST /api/v1/auth/login
{
  "email": "buyer@test.com",
  "password": "StrongP@ss123"
}
# → Returns: { "data": { "accessToken": "...", "refreshToken": "...", "expiresIn": 3600, "tokenType": "Bearer" } }

# 3. Refresh Token
POST /api/v1/auth/refresh
{
  "refreshToken": "rf_abc123..."
}

# 4. Revoke Token (requires JWT)
POST /api/v1/auth/revoke
Authorization: Bearer <token>
{
  "refreshToken": "rf_abc123..."
}

# 5. Validate Token
POST /api/v1/auth/validate
{
  "accessToken": "eyJhbG..."
}

# 6. Verify Email
GET /api/v1/auth/verify-email?token=<verification_token>

# 7. Resend Verification Email
POST /api/v1/auth/resend-verification
{
  "email": "buyer@test.com"
}

# 8. Request Password Reset
POST /api/v1/auth/password-reset/request
{
  "email": "buyer@test.com"
}

# 9. Confirm Password Reset
POST /api/v1/auth/password-reset/confirm?token=<reset_token>
{
  "newPassword": "NewStr0ng!Pass"
}
```

---

### User (9 endpoints — all require JWT)

```bash
# 10. Create User
POST /api/v1/users/
Authorization: Bearer <token>
{
  "fullName": "Test Buyer",
  "password": "StrongP@ss123",
  "email": "buyer@test.com",
  "phone": "+1234567890",
  "countryCode": "US",
  "customerTier": "Standard"
}

# 11. Get User
GET /api/v1/users/{userId}
Authorization: Bearer <token>

# 12. Update User
PUT /api/v1/users/{userId}
Authorization: Bearer <token>
{
  "fullName": "Updated Buyer Name",
  "email": "buyer@test.com",
  "phone": "+1234567890",
  "countryCode": "US",
  "customerTier": "Premium"
}

# 13. Delete User
DELETE /api/v1/users/{userId}
Authorization: Bearer <token>

# 14. Change Password
PUT /api/v1/users/{userId}/password
Authorization: Bearer <token>
{
  "currentPassword": "StrongP@ss123",
  "newPassword": "EvenStr0nger!456"
}

# 15. Block User
POST /api/v1/users/{userId}/block
Authorization: Bearer <token>

# 16. Assign Role
POST /api/v1/users/{userId}/roles
Authorization: Bearer <token>
{
  "roleName": "Seller"
}

# 17. Remove Role
DELETE /api/v1/users/{userId}/roles/Seller
Authorization: Bearer <token>

# 18. Get User Roles
GET /api/v1/users/{userId}/roles
Authorization: Bearer <token>
```

---

### Roles (5 endpoints — require JWT + Admin)

```bash
# 19. Create Role
POST /api/v1/roles/
Authorization: Bearer <admin_token>
{
  "name": "Seller"
}

# 20. List Roles
GET /api/v1/roles/
Authorization: Bearer <admin_token>

# 21. Get Role
GET /api/v1/roles/{roleId}
Authorization: Bearer <admin_token>

# 22. Update Role
PUT /api/v1/roles/{roleId}
Authorization: Bearer <admin_token>
{
  "name": "SuperSeller"
}

# 23. Delete Role
DELETE /api/v1/roles/{roleId}
Authorization: Bearer <admin_token>
```

---

### Products (5 endpoints — anonymous)

```bash
# 24. Create Product
POST /api/v1/products/
{
  "sellerId": "seller-uuid-here",
  "name": "Vintage Mechanical Keyboard",
  "description": "IBM Model M from 1989, fully working",
  "categoryId": "electronics",
  "price": 149.99,
  "currency": "USD",
  "initialStock": 5,
  "attributes": [
    { "name": "Brand", "value": "IBM" },
    { "name": "Year", "value": "1989" },
    { "name": "Condition", "value": "Used - Excellent" }
  ],
  "imageUrls": [
    "https://example.com/images/keyboard1.jpg"
  ]
}
# → Returns: { "data": { "productId": "...", "status": "PendingReview" } }

# 25. Get Product Details
GET /api/v1/products/{productId}

# 26. Get Products Batch
POST /api/v1/products/batch
{
  "productIds": ["product-id-1", "product-id-2"]
}

# 27. Get Product Prices
POST /api/v1/products/prices
{
  "productIds": ["product-id-1", "product-id-2"]
}

# 28. Get Product Status
POST /api/v1/products/{productId}/status
{
  "productId": "product-id",
  "sellerId": "seller-uuid-here"
}
```

---

### Listings (10 endpoints — anonymous)

```bash
# 29. Create Listing (add your offer to existing catalog item)
POST /api/v1/listings/
{
  "catalogItemId": "catalog-item-uuid",
  "sellerId": "seller-uuid-here",
  "price": 139.99,
  "currency": "USD",
  "initialStock": 3,
  "condition": "Used - Good",
  "sellerNotes": "Minor scratches on case, keys fully functional"
}

# 30. Get Listing Details
GET /api/v1/listings/{listingId}

# 31. Get Listings for Catalog Item (with pagination/sorting)
GET /api/v1/listings/catalog-item/{catalogItemId}?page=1&size=20&sortBy=price&condition=Used

# 32. Get Seller's Listings
GET /api/v1/listings/seller/{sellerId}?page=1&size=20

# 33. Update Listing + Catalog Item
PUT /api/v1/listings/{listingId}
{
  "name": "Updated Keyboard Name",
  "description": "Updated description",
  "categoryId": "electronics",
  "price": 159.99,
  "currency": "USD",
  "attributes": [
    { "name": "Brand", "value": "IBM" }
  ],
  "imageUrls": ["https://example.com/images/keyboard2.jpg"],
  "gtin": "1234567890123",
  "condition": "Used - Excellent",
  "sellerNotes": "Updated notes"
}

# 34. Activate Listing
POST /api/v1/listings/{listingId}/activate

# 35. Deactivate Listing
POST /api/v1/listings/{listingId}/deactivate

# 36. Delete Listing
DELETE /api/v1/listings/{listingId}

# 37. Update Stock
PUT /api/v1/listings/{listingId}/stock
{
  "newQuantity": 10
}

# 38. Change Price
PUT /api/v1/listings/{listingId}/price
{
  "price": 129.99,
  "currency": "USD"
}
```

---

### Search (4 endpoints — anonymous)

```bash
# 39. Search Products
GET /api/v1/search/?q=mechanical+keyboard&page=1&pageSize=20&useAi=false
# With AI search:
GET /api/v1/search/?q=vintage+keyboard+under+200&page=1&pageSize=20&useAi=true&userId=user-uuid

# 40. Similar Items
GET /api/v1/search/similar/{catalogItemId}?limit=10&category=electronics&condition=Used

# 41. Frequently Bought Together
GET /api/v1/search/frequently-bought-together/{catalogItemId}?limit=5

# 42. Stream Search (Server-Sent Events)
GET /api/v1/search/stream?q=mechanical+keyboard&page=1&pageSize=20&userId=user-uuid
# → Returns text/event-stream with progressive results
```

---

### Orders (5 endpoints — require JWT)

```bash
# 43. Create Order
POST /api/v1/orders/
Authorization: Bearer <token>
{
  "customerId": "buyer-uuid",
  "items": [
    {
      "productId": "product-uuid",
      "quantity": 1,
      "price": 149.99,
      "currency": "USD"
    }
  ],
  "deliveryAddress": {
    "street": "123 Main St",
    "city": "San Francisco",
    "country": "US",
    "postalCode": "94102"
  },
  "paymentMethod": "stripe",
  "idempotencyKey": "order-idem-key-unique-123"
}
# → Returns: { "data": { "success": true, "orderId": "...", "errorMessage": null } }

# 44. Get Order Details
GET /api/v1/orders/{orderId}
Authorization: Bearer <token>

# 45. List Orders (paginated)
GET /api/v1/orders/?pageNumber=1&pageSize=20
Authorization: Bearer <token>

# 46. Get Customer Orders
GET /api/v1/orders/customer/{customerId}
Authorization: Bearer <token>

# 47. Request Return
POST /api/v1/orders/{orderId}/return
Authorization: Bearer <token>
{
  "reason": "Item not as described",
  "itemsToReturn": [
    {
      "productId": "product-uuid",
      "quantity": 1,
      "price": 149.99,
      "currency": "USD"
    }
  ],
  "idempotencyKey": "return-idem-key-unique-456"
}
```

---

### B2B Orders (5 endpoints — require JWT)

```bash
# 48. Start B2B Order
POST /api/v1/b2b-orders/
Authorization: Bearer <token>
{
  "customerId": "buyer-uuid",
  "companyName": "Acme Corp",
  "deliveryAddress": {
    "street": "456 Business Ave",
    "city": "New York",
    "country": "US",
    "postalCode": "10001"
  },
  "idempotencyKey": "b2b-start-unique-789"
}

# 49. Get B2B Order
GET /api/v1/b2b-orders/{b2bOrderId}
Authorization: Bearer <token>

# 50. Update Quote Draft
PATCH /api/v1/b2b-orders/{b2bOrderId}/quote
Authorization: Bearer <token>
{
  "changes": [
    { "productId": "product-uuid", "quantity": 100, "unitPrice": 120.00, "currency": "USD" }
  ],
  "comment": "Bulk discount applied",
  "commentAuthor": "sales-rep-uuid"
}

# 51. Finalize Quote → Create Order
POST /api/v1/b2b-orders/{b2bOrderId}/finalize
Authorization: Bearer <token>
{
  "paymentMethod": "invoice_net30",
  "idempotencyKey": "b2b-finalize-unique-101"
}

# 52. Cancel B2B Order
POST /api/v1/b2b-orders/{b2bOrderId}/cancel
Authorization: Bearer <token>
{
  "reasons": ["Customer changed requirements", "Budget not approved"]
}
```

---

### Recurring Orders (6 endpoints — require JWT)

```bash
# 53. Create Recurring Order
POST /api/v1/recurring-orders/
Authorization: Bearer <token>
{
  "customerId": "buyer-uuid",
  "paymentMethod": "stripe",
  "frequency": "Monthly",
  "items": [
    {
      "productId": "product-uuid",
      "quantity": 2,
      "price": 29.99,
      "currency": "USD"
    }
  ],
  "deliveryAddress": {
    "street": "123 Main St",
    "city": "San Francisco",
    "country": "US",
    "postalCode": "94102"
  },
  "firstRunAt": "2026-07-01T00:00:00Z",
  "maxExecutions": 12,
  "idempotencyKey": "recurring-unique-202"
}

# 54. Get Recurring Order
GET /api/v1/recurring-orders/{recurringOrderId}
Authorization: Bearer <token>

# 55. Get Customer's Recurring Orders
GET /api/v1/recurring-orders/customer/{customerId}
Authorization: Bearer <token>

# 56. Pause Recurring Order
POST /api/v1/recurring-orders/{recurringOrderId}/pause
Authorization: Bearer <token>

# 57. Resume Recurring Order
POST /api/v1/recurring-orders/{recurringOrderId}/resume
Authorization: Bearer <token>

# 58. Cancel Recurring Order
POST /api/v1/recurring-orders/{recurringOrderId}/cancel
Authorization: Bearer <token>
{
  "reason": "No longer needed"
}
```

---

### Payments (2 endpoints — require JWT)

```bash
# 59. Get Payment by ID
GET /api/v1/payments/{paymentId}
Authorization: Bearer <token>

# 60. Get Payment by Order
GET /api/v1/payments/order/{orderId}?idempotencyKey=order-idem-key-unique-123
Authorization: Bearer <token>
```

---

### Inventory (2 endpoints — require JWT)

```bash
# 61. Reserve Inventory
POST /api/v1/inventory/reserve
Authorization: Bearer <token>
{
  "orderId": "order-uuid",
  "items": [
    { "productId": "product-uuid", "quantity": 1 }
  ]
}

# 62. Release Inventory
POST /api/v1/inventory/release
Authorization: Bearer <token>
{
  "reservationId": "reservation-uuid"
}
```

---

### User Events (4 endpoints — require JWT)

```bash
# 63. Product Viewed
POST /api/v1/user-events/view
Authorization: Bearer <token>
{
  "catalogItemId": "catalog-item-uuid",
  "durationMs": 15000,
  "source": "search",
  "category": "electronics",
  "brand": "IBM",
  "price": 149.99,
  "condition": "Used"
}
# → 202 Accepted (fires Kafka event to UserPreferenceWorker)

# 64. Product Clicked
POST /api/v1/user-events/click
Authorization: Bearer <token>
{
  "catalogItemId": "catalog-item-uuid",
  "queryText": "mechanical keyboard",
  "rank": 3,
  "category": "electronics",
  "brand": "IBM",
  "price": 149.99,
  "condition": "Used"
}

# 65. Purchase Completed
POST /api/v1/user-events/purchase
Authorization: Bearer <token>
{
  "catalogItemId": "catalog-item-uuid",
  "listingId": "listing-uuid",
  "price": 149.99,
  "category": "electronics",
  "brand": "IBM",
  "condition": "Used"
}

# 66. Search Bounced
POST /api/v1/user-events/search-bounce
Authorization: Bearer <token>
{
  "queryText": "broken search query with no results"
}
```

---

### Health (2 endpoints — anonymous)

```bash
# 67. Liveness
GET /health/live

# 68. Readiness
GET /health/ready
```

---

### ProductAdmin Service (separate service, direct access)

```bash
# 69. Approve Product
POST http://localhost:5050/products/{productId}/approve
X-Admin-Api-Key: <admin-key>

# 70. Reject Product
POST http://localhost:5050/products/{productId}/reject
X-Admin-Api-Key: <admin-key>
{
  "reason": "Inappropriate content in description"
}

# 71. Get Catalog Items (ProductAdmin also has catalog item management endpoints)
# (check ProductAdmin/Endpoints/ for additional admin-only catalog endpoints)
```

---

---

## Use-Case Flows (Correct Order)

### Flow 1: Basic Purchase (Happy Path)

The **complete buyer journey** from registration to order completion:

```
Step 1: Register → Step 2: Verify Email → Step 3: Login → Step 4: Create Product (seller)
→ Step 5: Approve Product (admin) → Step 6: Search → Step 7: View Product 
→ Step 8: Create Order → Step 9: Check Order Status → Step 10: Track Payment
```

| Step | Endpoint | Why This Order |
|------|----------|----------------|
| 1 | `POST /api/v1/auth/register` | Creates auth credentials + triggers email verification via Kafka |
| 2 | `GET /api/v1/auth/verify-email?token=...` | Email service sends verification link; user must verify before full access |
| 3 | `POST /api/v1/auth/login` | Returns JWT + refresh token |
| 4 | `POST /api/v1/products/` | Seller creates product → status = `PendingReview`, publishes ProductCreatedEvent to Kafka |
| 5 | `POST http://localhost:5050/products/{id}/approve` | Admin approves → Catalog Consumer indexes to Elasticsearch, VectorIndexer embeds to Qdrant |
| 6 | `GET /api/v1/search/?q=keyboard` | Searches Elasticsearch (and optionally AI/Qdrant) |
| 7 | `POST /api/v1/user-events/view` | Records user interest → UserPreferenceWorker updates Redis profile |
| 8 | `POST /api/v1/orders/` | **Triggers 8-step saga**: ValidateOrder → ReserveInventory → AuthorizePayment → WaitForPaymentConfirm → ConfirmInventory → UpdateProductStock → NotifyUser → CompleteOrder |
| 9 | `GET /api/v1/orders/{orderId}` | Check saga progress (Pending → Processing → Completed) |
| 10 | `GET /api/v1/payments/order/{orderId}` | See payment status (Authorized → Captured) |

---

### Flow 2: Seller Onboarding + Marketplace Model

```
Register Seller → Login → Create Product → Get Status (PendingReview) → Admin Approves 
→ Product goes Active → Shows in Search → Other sellers add Listings to same CatalogItem
```

| Step | Endpoint | Notes |
|------|----------|-------|
| 1 | `POST /api/v1/auth/register` | `email: "seller@shop.com"` |
| 2 | `GET /api/v1/auth/verify-email?token=...` | — |
| 3 | `POST /api/v1/auth/login` | — |
| 4 | `POST /api/v1/users/{id}/roles` | Assign "Seller" role |
| 5 | `POST /api/v1/products/` | Creates Product + CatalogItem + initial Listing |
| 6 | `POST /api/v1/products/{id}/status` | Check: should be `PendingReview` |
| 7 | `POST :5050/products/{id}/approve` | Admin approval → status = Active |
| 8 | `GET /api/v1/search/?q=...` | Product now searchable |
| 9 | `POST /api/v1/listings/` | **Second seller** adds their listing to same CatalogItem (eBay model) |
| 10 | `GET /api/v1/listings/catalog-item/{id}` | Multiple sellers, sorted by price |

---

### Flow 3: AI-Powered Search + Personalization

```
Browse products → Record views/clicks → Search with AI → Get personalized results 
→ Find similar items → See "frequently bought together"
```

| Step | Endpoint | Notes |
|------|----------|-------|
| 1 | `POST /api/v1/user-events/view` | View a few electronics products |
| 2 | `POST /api/v1/user-events/click` | Click from search results (records rank) |
| 3 | `POST /api/v1/user-events/purchase` | Complete a purchase |
| 4 | — | *(Wait: UserPreferenceWorker aggregates profile in Redis)* |
| 5 | `GET /api/v1/search/?q=best+keyboard+under+200&useAi=true&userId=...` | AI parses query → vector + keyword search → RRF → personalized rerank based on profile |
| 6 | `GET /api/v1/search/similar/{catalogItemId}` | Vector similarity in Qdrant |
| 7 | `GET /api/v1/search/frequently-bought-together/{catalogItemId}` | Redis co-occurrence sorted sets |

---

### Flow 4: Order Failure + Compensation (Saga Rollback)

```
Create Order → Inventory reserved → Payment fails → Saga compensates 
→ Inventory released → Order marked Failed
```

| Step | Endpoint | Notes |
|------|----------|-------|
| 1 | `POST /api/v1/orders/` | Start order saga |
| 2 | `GET /api/v1/orders/{orderId}` | Status: `Processing` (inventory reserved) |
| 3 | — | *(Payment service rejects: insufficient funds)* |
| 4 | `GET /api/v1/orders/{orderId}` | Status: `Failed` — compensation ran |
| 5 | `POST /api/v1/inventory/release` | *(Saga does this automatically — verify stock returned)* |

---

### Flow 5: B2B Quote → Order Lifecycle

```
Start B2B order → Negotiate quote → Finalize → Creates regular Order → Saga runs
```

| Step | Endpoint | Notes |
|------|----------|-------|
| 1 | `POST /api/v1/b2b-orders/` | Status: `Draft` |
| 2 | `PATCH /api/v1/b2b-orders/{id}/quote` | Add items, set bulk pricing |
| 3 | `PATCH /api/v1/b2b-orders/{id}/quote` | Revise (multiple rounds) |
| 4 | `POST /api/v1/b2b-orders/{id}/finalize` | Creates regular Order + starts saga |
| 5 | `GET /api/v1/orders/{orderId}` | Check the resulting order |

---

### Flow 6: Recurring Order

```
Create recurring → Scheduler fires monthly → Creates Order each time → Pause → Resume → Cancel
```

| Step | Endpoint | Notes |
|------|----------|-------|
| 1 | `POST /api/v1/recurring-orders/` | frequency: "Monthly", maxExecutions: 12 |
| 2 | `GET /api/v1/recurring-orders/{id}` | Status: Active, nextRunAt shows schedule |
| 3 | — | *(RecurringOrderScheduler with FOR UPDATE SKIP LOCKED fires)* |
| 4 | `GET /api/v1/orders/customer/{customerId}` | New order appears each period |
| 5 | `POST /api/v1/recurring-orders/{id}/pause` | Suspends scheduling |
| 6 | `POST /api/v1/recurring-orders/{id}/resume` | Resumes |
| 7 | `POST /api/v1/recurring-orders/{id}/cancel` | Permanent stop |

---

### Flow 7: Return Request

```
Order completed → Request return → Return saga runs (hardcoded, non-functional)
```

| Step | Endpoint | Notes |
|------|----------|-------|
| 1 | `GET /api/v1/orders/{orderId}` | Verify status = Completed |
| 2 | `POST /api/v1/orders/{orderId}/return` | Starts return saga |
| 3 | `GET /api/v1/orders/{orderId}` | Status should update (⚠️ return saga is hardcoded/broken) |

---

---

## What's MISSING (No Endpoints Exist)

| Feature | Impact | Notes |
|---------|--------|-------|
| **🛒 Cart / Basket** | Critical | No add-to-cart, view cart, update quantity. Orders go from "nothing" → full order. |
| **💰 Checkout Flow** | Critical | No pre-order price calculation, shipping estimate, or payment method selection UI flow. |
| **📦 Shipping/Fulfillment** | Major | No shipment tracking, carrier integration, delivery status updates. |
| **⭐ Reviews / Ratings** | Medium | No product reviews, seller ratings, or trust system. |
| **❤️ Wishlist / Favorites** | Medium | User events track views but no explicit save-for-later. |
| **🔔 Notifications** | Medium | Email service exists but no in-app notifications, push, or notification preferences. |
| **📊 Seller Dashboard** | Medium | Sellers can create products but can't see sales, revenue, or analytics. |
| **🏷️ Promotions / Coupons** | Low | No discount codes, sales events, or promotional pricing. |
| **💬 Buyer-Seller Messaging** | Low | No communication channel between parties. |
| **📋 Order History Export** | Low | No CSV/PDF invoice generation. |

---

## Correct E2E Test Script (curl)

Paste into terminal to test the full happy path:

```bash
BASE=http://localhost:5081
ADMIN=http://localhost:5050

# === STEP 1: Register buyer ===
curl -s -X POST $BASE/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "buyer@test.com",
    "password": "Test123!@#",
    "fullName": "Test Buyer",
    "phone": "+1555000001"
  }'

# === STEP 2: Register seller ===
curl -s -X POST $BASE/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "seller@test.com",
    "password": "Test123!@#",
    "fullName": "Test Seller",
    "phone": "+1555000002"
  }'

# === STEP 3: Login as seller ===
SELLER_TOKEN=$(curl -s -X POST $BASE/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email": "seller@test.com", "password": "Test123!@#"}' \
  | jq -r '.data.accessToken')

echo "Seller token: $SELLER_TOKEN"

# === STEP 4: Create product (as seller) ===
PRODUCT_RESPONSE=$(curl -s -X POST $BASE/api/v1/products/ \
  -H "Content-Type: application/json" \
  -d '{
    "sellerId": "seller-id-from-registration",
    "name": "Vintage Mechanical Keyboard",
    "description": "IBM Model M 1989, fully working, clicky switches",
    "categoryId": "electronics",
    "price": 149.99,
    "currency": "USD",
    "initialStock": 5,
    "attributes": [
      {"name": "Brand", "value": "IBM"},
      {"name": "Year", "value": "1989"},
      {"name": "Switch Type", "value": "Buckling Spring"}
    ],
    "imageUrls": ["https://example.com/keyboard.jpg"]
  }')

PRODUCT_ID=$(echo $PRODUCT_RESPONSE | jq -r '.data.productId')
echo "Product ID: $PRODUCT_ID"

# === STEP 5: Admin approves product ===
curl -s -X POST $ADMIN/products/$PRODUCT_ID/approve \
  -H "X-Admin-Api-Key: your-admin-key-here"

# === STEP 6: Wait for Kafka → Catalog consumer → Elasticsearch ===
sleep 3

# === STEP 7: Search for the product ===
curl -s "$BASE/api/v1/search/?q=mechanical+keyboard&page=1&pageSize=10"

# === STEP 8: Login as buyer ===
BUYER_TOKEN=$(curl -s -X POST $BASE/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email": "buyer@test.com", "password": "Test123!@#"}' \
  | jq -r '.data.accessToken')

# === STEP 9: Record product view (for personalization) ===
curl -s -X POST $BASE/api/v1/user-events/view \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -d '{
    "catalogItemId": "'$PRODUCT_ID'",
    "durationMs": 30000,
    "source": "search",
    "category": "electronics",
    "brand": "IBM",
    "price": 149.99
  }'

# === STEP 10: Create order (triggers saga) ===
ORDER_RESPONSE=$(curl -s -X POST $BASE/api/v1/orders/ \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -d '{
    "customerId": "buyer-id-from-registration",
    "items": [
      {"productId": "'$PRODUCT_ID'", "quantity": 1, "price": 149.99, "currency": "USD"}
    ],
    "deliveryAddress": {
      "street": "123 Main St",
      "city": "San Francisco",
      "country": "US",
      "postalCode": "94102"
    },
    "paymentMethod": "stripe",
    "idempotencyKey": "test-order-'$(date +%s)'"
  }')

ORDER_ID=$(echo $ORDER_RESPONSE | jq -r '.data.orderId')
echo "Order ID: $ORDER_ID"

# === STEP 11: Poll order status (saga executing) ===
sleep 5
curl -s "$BASE/api/v1/orders/$ORDER_ID" \
  -H "Authorization: Bearer $BUYER_TOKEN" | jq '.data.status'

# === STEP 12: Check payment ===
curl -s "$BASE/api/v1/payments/order/$ORDER_ID" \
  -H "Authorization: Bearer $BUYER_TOKEN" | jq '.data'
```

---

## System Dependency Chain (What Triggers What)

```
Register (Auth) ──Kafka──→ Email Service (sends verification)
                          └──→ User Service (creates profile)

Create Product ──Kafka──→ Catalog Consumer (indexes ES)
                        └──→ VectorIndexerWorker (embeds Qdrant) ⚠️ BROKEN
                        └──→ Inventory Service (creates stock record)

Create Order ──Saga──→ Inventory.Reserve (gRPC)
                     → Payment.Authorize (gRPC) → Stripe
                     → [Wait for webhook/reconciliation]
                     → Inventory.Confirm (gRPC)
                     → Product.UpdateStock (Kafka)
                     → Email.OrderConfirmation (Kafka)

User Events ──Kafka──→ UserPreferenceWorker (Redis profile)
                     └──→ Co-occurrence tracking (Redis sorted sets)
```
