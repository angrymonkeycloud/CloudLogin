# CloudLogin workspaces

CloudLogin owns account identity infrastructure: users, authentication, profiles, security, and workspaces (members, owners, roles, permissions, invitations). Nothing commercial lives here.

## Workspace registry

`ICloudLoginWorkspaceRegistry` creates and manages workspaces, memberships, invitations, quotas, and deletion. `CloudWorkspace` carries the workspace profile (name, legal name, website, tax id, billing-contact information) — profile data an application may display or sync, not commercial state.

## Commercial boundary

Subscriptions, orders, and payments do not live in CloudLogin:

- Each application/division owns its subscriptions in full (plan, tokens, renewal, status, limits).
- Angry Monkey (the group system) owns centralized orders and payment history across all divisions.
- Angry Monkey Pay handles payment checkout; CloudPayments owns provider communication and transaction execution.

CloudLogin contributes only identity: applications record `CloudUserId`/`CloudWorkspaceId` on their own commercial records and authenticate people through CloudLogin.

## Workspace deletion

Deleting a workspace removes its memberships and invitations in one operation and publishes `Workspace.Deleted`. Since no commercial records live in CloudLogin, nothing here blocks deletion; `CloudWorkspaceDeletionReport` tells the account page how many other members lose access.
