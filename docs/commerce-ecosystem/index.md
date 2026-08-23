# Angry Monkey Cloud commerce ecosystem architecture

The Angry Monkey Cloud commerce ecosystem provides independent payment, logistics, and booking foundations plus an optional commerce orchestration layer. The projects are hosted in the CloudLogin workspace temporarily but keep extractable package boundaries.

## Dependency map

```text
CloudCommerce.Components
        |
        v
CloudCommerce ---> CloudPayments
      |  |-------> CloudLogistics ---> CloudGeography
      |  |-------> CloudBooking
      |----------> CloudLogin.Contracts

CloudPayments -----------------------> CloudGeography
      ^
Stripe / PayPal / Adyen / MyFatoorah / SkipCash / Tap / PayTabs

CloudLogistics <--- Aramex / DHL Express / FedEx

Public contracts <--- private CDM.CloudCommerce adapters ---> CDM storage
```

CloudPayments, CloudLogistics, and CloudBooking never reference CloudCommerce. No public project references CDM. Applications keep their authoritative product, service, plan, and subscription semantics.

## Existing Angry Monkey Cloud capabilities reused

- CloudLogin remains the identity and account boundary. Its lightweight workspace, membership, billing-reference, and subscription registries carry account state without executing payments or defining application entitlements.
- CloudGeography supplies money, countries, subdivisions, and time-zone data. CloudLogistics only adds the postal fields missing from that geographic dataset and stores CloudGeography country/subdivision codes.
- CloudComponents remains the reusable Blazor component infrastructure. CloudCommerce Components uses the active component project rather than creating a competing framework.
- CloudComponents.Maps is the current map package. There is no separate CloudMaps repository in the supplied workspace; location UI can compose that package when a map is required.
- CDM continues to own private Cosmos, Azure Storage, validation, form, grid, and dashboard infrastructure. Private adapters map public store contracts to CDM records.

## Conflict decisions

CDM already contains private organization, business-unit, and role storage. Those types remain implementation details and do not become public identity contracts. CloudLogin now defines the public workspace and membership semantics, and a private adapter maps them when CDM persistence is selected.

CloudGeography does not expose a postal-address contract. `LogisticsAddress` therefore contains delivery lines and locality while referencing CloudGeography codes instead of duplicating country or subdivision entities.

## Domain documentation

- [CloudLogin workspaces and subscriptions](../account-registry.md)
- [CloudPayments](../../CloudPayments/docs/index.md)
- [Payment providers](../../CloudPayments/docs/providers.md)
- [Shipping carriers](../../CloudLogistics/docs/carriers.md)
- [CloudCommerce demo](../../CloudCommerce/docs/demo.md)
- [CloudLogistics](../../CloudLogistics/docs/index.md)
- [CloudBooking](../../CloudBooking/docs/index.md)
- [CloudCommerce](../../CloudCommerce/docs/index.md)
- [CloudCommerce UI customization](../../CloudCommerce/docs/ui.md)
- [Private CDM adapters](../../../../CDM/CDM.CloudCommerce/docs/index.md)
