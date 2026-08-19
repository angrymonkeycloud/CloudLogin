# CloudLogin organizations and lightweight subscriptions

CloudLogin owns account identity infrastructure for organizations, members, owners, roles, permissions, invitations, billing references, and lightweight subscription registry entries. These contracts remain usable without CloudCommerce.

## Organization registry

`IOrganizationRegistry` creates organizations, records owner membership, adds members, and creates invitations. `CloudLoginOrganizationMember` stores membership state plus application-defined role and permission codes. CloudLogin does not use commerce orders to infer identity ownership.

Register the default in-memory implementation for local use and isolated tests:

```csharp
services.AddCloudLoginAccountRegistry();
```

CDM hosts call `AddCdmCloudCommerceAdapters()` after this registration to replace only `ICloudLoginAccountStore` with the private CDM-backed adapter.

## Subscription registry boundary

`ISubscriptionRegistry` answers whether a user or organization has an active application subscription and returns active registry entries. `AccountSubscription` records the application, reference or SKU, expiry, auto-renew flag, provider references, and structured application metadata.

The application owns plan semantics, entitlements, usage, credits, renewal decisions, and top-up behavior. CloudLogin does not execute recurring payments. CloudPayments owns provider communication and transaction execution.

## Billing references

`AccountBillingProfile` stores provider customer and payment-method references. It never captures, refunds, or charges a payment method. Applications pass those references to CloudPayments when an actual transaction is required.

See the [commerce ecosystem architecture](commerce-ecosystem/index.md), [CloudPayments](../CloudPayments/docs/index.md), and [CloudCommerce](../CloudCommerce/docs/index.md).
