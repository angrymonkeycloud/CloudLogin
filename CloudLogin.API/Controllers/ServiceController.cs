using System.Globalization;
using System.Text.Json;
using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.Controllers;

/// <summary>
/// Server-to-server lookups and writes for trusted backend callers (e.g. AngryMonkey.Portal),
/// gated by the "ServiceKey" scheme only — never reachable via a browser cookie session.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not version-gated. This is the authority's own trusted backend channel, like its
/// UI and flow endpoints, rather than one of the versioned public façades — and
/// <c>api/v3/service</c> covers only <c>users/{id}</c>, so gating this to V2 removed workspace and
/// membership reads that have no replacement. With the API version defaulting to V3 the gate made
/// every <c>CloudLogin/Service/*</c> route answer 404 to a correctly authenticated caller, which
/// reads to the calling component as its data simply not being there.
/// </para>
/// <para>
/// Writes are whitelisted field by field, and the whitelist is the profile: everything a workspace
/// or a user carries about itself. What it never includes is anything the server manages —
/// sign-in identifiers, lock state, privileges, ownership. A backend syncing fields must not become
/// a back door to those, which each have their own deliberate flow. Anything outside the whitelist
/// is rejected rather than silently ignored, so a caller finds out immediately.
/// </para>
/// </remarks>
[Route("CloudLogin/Service")]
[ApiController]
[Authorize(AuthenticationSchemes = ServiceKeyAuthenticationDefaults.AuthenticationScheme)]
public class ServiceController(CloudLoginWebConfiguration configuration, ICloudLogin server) : CloudLoginBaseController(configuration, server)
{
    private static readonly JsonSerializerOptions AddressOptions = new() { PropertyNameCaseInsensitive = true };

    [HttpGet("Workspaces")]
    public async Task<ActionResult<List<CloudWorkspace>>> GetAllWorkspaces()
    {
        try
        {
            return Ok(await _server.GetAllWorkspaces());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Workspaces/{workspaceId:guid}")]
    public async Task<ActionResult<CloudWorkspace>> GetWorkspace(Guid workspaceId)
    {
        try
        {
            CloudWorkspace? workspace = await _server.GetWorkspaceById(workspaceId);

            if (workspace == null)
                return NotFound();

            return Ok(workspace);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Workspaces/{workspaceId:guid}/Members")]
    public async Task<ActionResult<List<CloudWorkspaceMember>>> GetWorkspaceMembers(Guid workspaceId)
    {
        try
        {
            return Ok(await _server.GetWorkspaceMembers(workspaceId));
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Users")]
    public async Task<ActionResult<List<CloudUser>>> GetAllUsers()
    {
        try
        {
            return Ok(await _server.GetAllUsers());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Users/{userId:guid}")]
    public async Task<ActionResult<CloudUser>> GetUser(Guid userId)
    {
        try
        {
            CloudUser? user = await _server.GetUserById(userId);

            if (user == null)
                return NotFound();

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }

    /// <summary>
    /// The two lookups a trusted backend needs to resolve a candidate for its own access-grant UI
    /// (someone typed a name or an email; which account is that?) without holding an end-user
    /// session. The interactive equivalents exist too, but one requires the caller to already be a
    /// CloudLogin global admin and the other is deliberately public/anonymous-safe — neither fits a
    /// backend that has its own, separate notion of "admin" and wants the full profile back.
    /// </summary>
    [HttpGet("Users/ByDisplayName")]
    public async Task<ActionResult<List<CloudUser>>> GetUsersByDisplayName(string displayName)
    {
        try
        {
            return Ok(await _server.GetUsersByDisplayName(displayName));
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Users/ByEmail")]
    public async Task<ActionResult<CloudUser>> GetUserByEmail(string email)
    {
        try
        {
            CloudUser? user = await _server.GetUserByEmailAddress(email);

            if (user == null)
                return NotFound();

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }

    /// <summary>
    /// Partial update of a workspace's profile: its name, legal name, website, phone, billing
    /// contact, tax id and billing address — every field the workspace carries about itself.
    /// Ownership is not a profile field and stays where it is.
    /// </summary>
    [HttpPut("Workspaces/{workspaceId:guid}")]
    public async Task<ActionResult<CloudWorkspace>> UpdateWorkspace(Guid workspaceId, [FromBody] Dictionary<string, JsonElement> values)
    {
        try
        {
            CloudWorkspace? workspace = await _server.GetWorkspaceById(workspaceId);

            if (workspace == null)
                return NotFound();

            if (ApplyWorkspaceFields(workspace, values) is { } rejected)
                return BadRequest(rejected);

            return Ok(await _server.UpdateWorkspaceAsService(workspace));
        }
        catch
        {
            return Problem();
        }
    }

    /// <summary>
    /// Creates a workspace for a record that was born elsewhere (a CDM Business). There is no
    /// signed-in user to own it, so the caller names the owner, who must be an existing CloudLogin
    /// user; the owner's workspace allowance applies exactly as it does on the account page. The
    /// profile fields <see cref="UpdateWorkspace"/> accepts may come along in the same request.
    /// </summary>
    [HttpPost("Workspaces")]
    public async Task<ActionResult<CloudWorkspace>> CreateWorkspace([FromBody] Dictionary<string, JsonElement> values)
    {
        try
        {
            string? name = values.TryGetValue(nameof(CloudWorkspace.Name), out JsonElement nameValue) ? Text(nameValue) : null;

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("A workspace needs a Name.");

            if (!values.TryGetValue(nameof(CloudWorkspace.OwnerUserId), out JsonElement ownerValue)
                || !Guid.TryParse(Text(ownerValue), out Guid ownerUserId))
                return BadRequest("A workspace needs an OwnerUserId: the CloudLogin user who will own it.");

            if (await _server.GetUserById(ownerUserId) is null)
                return BadRequest($"OwnerUserId '{ownerUserId}' is not a CloudLogin user.");

            Dictionary<string, JsonElement> profile = values
                .Where(pair => pair.Key is not (nameof(CloudWorkspace.Name) or nameof(CloudWorkspace.OwnerUserId)))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            // Validated before anything is created, so a rejected field never leaves a half-made
            // workspace behind.
            if (ApplyWorkspaceFields(new CloudWorkspace { Name = name }, profile) is { } rejected)
                return BadRequest(rejected);

            CloudWorkspace workspace = await _server.CreateWorkspaceAsService(name.Trim(), ownerUserId);

            if (profile.Count == 0)
                return Ok(workspace);

            ApplyWorkspaceFields(workspace, profile);

            return Ok(await _server.UpdateWorkspaceAsService(workspace));
        }
        catch (CloudWorkspaceLimitReachedException exception)
        {
            return Conflict(exception.Message);
        }
        catch
        {
            return Problem();
        }
    }

    /// <summary>
    /// Partial update of a user's own profile fields - the same whitelist
    /// <see cref="UserController.Update"/> already applies to an end-user's own edit. Identifiers
    /// (email, phone, username) and lock state are deliberately excluded here too: they are
    /// server-managed, not something a backend caller should be able to overwrite by way of a CDM
    /// field sync.
    /// </summary>
    [HttpPut("Users/{userId:guid}")]
    public async Task<ActionResult<CloudUser>> UpdateUser(Guid userId, [FromBody] Dictionary<string, JsonElement> values)
    {
        try
        {
            CloudUser? user = await _server.GetUserById(userId);

            if (user == null)
                return NotFound();

            if (ApplyUserFields(user, values) is { } rejected)
                return BadRequest(rejected);

            await _server.UpdateUser(user);

            return Ok(await _server.GetUserById(userId));
        }
        catch
        {
            return Problem();
        }
    }

    /// <summary>
    /// Creates a user for a record that was born elsewhere (a CDM Contact). A user is only useful
    /// if they can sign in, so a primary email or phone number is required; an identifier that
    /// already belongs to someone is a conflict, never a silent merge. The profile fields
    /// <see cref="UpdateUser"/> accepts may come along in the same request. The new user has no
    /// credential and no privileges: they sign in through whichever provider verifies the
    /// identifier, exactly like anyone who registers themselves.
    /// </summary>
    [HttpPost("Users")]
    public async Task<ActionResult<CloudUser>> CreateUser([FromBody] Dictionary<string, JsonElement> values)
    {
        try
        {
            string? email = values.TryGetValue("PrimaryEmail", out JsonElement emailValue) ? Text(emailValue)?.Trim() : null;
            string? phone = values.TryGetValue("PrimaryPhone", out JsonElement phoneValue) ? Text(phoneValue)?.Trim() : null;

            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
                return BadRequest("A user needs a PrimaryEmail or a PrimaryPhone to sign in with.");

            if (!string.IsNullOrWhiteSpace(email) && await _server.GetUserByEmailAddress(email) is not null)
                return Conflict($"A user with the email '{email}' already exists.");

            if (!string.IsNullOrWhiteSpace(phone) && await _server.GetUserByPhoneNumber(phone) is not null)
                return Conflict($"A user with the phone number '{phone}' already exists.");

            CloudUser user = new()
            {
                ID = Guid.NewGuid(),
                CreatedOn = DateTimeOffset.UtcNow
            };

            Dictionary<string, JsonElement> profile = values
                .Where(pair => pair.Key is not ("PrimaryEmail" or "PrimaryPhone"))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            if (ApplyUserFields(user, profile) is { } rejected)
                return BadRequest(rejected);

            if (!string.IsNullOrWhiteSpace(email))
                user.Inputs.Add(new CloudLoginInput
                {
                    Format = CloudLoginInputFormat.EmailAddress,
                    Input = email,
                    IsPrimary = true
                });

            if (!string.IsNullOrWhiteSpace(phone))
                user.Inputs.Add(new CloudLoginInput
                {
                    Format = CloudLoginInputFormat.PhoneNumber,
                    Input = phone,
                    IsPrimary = string.IsNullOrWhiteSpace(email)
                });

            await _server.CreateUser(user);

            return Ok(await _server.GetUserById(user.ID) ?? user);
        }
        catch
        {
            return Problem();
        }
    }

    /// <summary>
    /// Applies the whitelisted workspace profile fields, answering with the reason when a field is
    /// outside it - and nothing is applied in that case only because every caller validates on a
    /// scratch copy first when it matters.
    /// </summary>
    private static string? ApplyWorkspaceFields(CloudWorkspace workspace, IEnumerable<KeyValuePair<string, JsonElement>> values)
    {
        foreach ((string key, JsonElement value) in values)
        {
            switch (key)
            {
                case nameof(CloudWorkspace.Name): workspace.Name = Text(value) is { Length: > 0 } name ? name : workspace.Name; break;
                case nameof(CloudWorkspace.LegalName): workspace.LegalName = Text(value); break;
                case nameof(CloudWorkspace.Website): workspace.Website = Text(value); break;
                case nameof(CloudWorkspace.Phone): workspace.Phone = Text(value); break;
                case nameof(CloudWorkspace.BillingEmail): workspace.BillingEmail = Text(value); break;
                case nameof(CloudWorkspace.BillingContactName): workspace.BillingContactName = Text(value); break;
                case nameof(CloudWorkspace.TaxId): workspace.TaxId = Text(value); break;
                case nameof(CloudWorkspace.BillingAddress): workspace.BillingAddress = Address(value); break;
                default: return $"Field '{key}' cannot be updated through the service endpoint.";
            }
        }

        return null;
    }

    /// <summary>See <see cref="ApplyWorkspaceFields"/>.</summary>
    private static string? ApplyUserFields(CloudUser user, IEnumerable<KeyValuePair<string, JsonElement>> values)
    {
        foreach ((string key, JsonElement value) in values)
        {
            switch (key)
            {
                case nameof(CloudUser.FirstName): user.FirstName = Text(value); break;
                case nameof(CloudUser.LastName): user.LastName = Text(value); break;
                case nameof(CloudUser.DisplayName): user.DisplayName = Text(value); break;
                case nameof(CloudUser.Country): user.Country = Text(value); break;
                case nameof(CloudUser.Locale): user.Locale = Text(value); break;
                case nameof(CloudUser.DateOfBirth):
                    string? date = Text(value);

                    if (string.IsNullOrWhiteSpace(date))
                        user.DateOfBirth = null;
                    else if (DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly dateOfBirth))
                        user.DateOfBirth = dateOfBirth;
                    else if (DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset moment))
                        user.DateOfBirth = DateOnly.FromDateTime(moment.Date);
                    else
                        return $"'{date}' is not a date. DateOfBirth takes yyyy-MM-dd.";

                    break;
                default: return $"Field '{key}' cannot be updated through the service endpoint.";
            }
        }

        return null;
    }

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        _ => value.ToString()
    };

    private static CloudWorkspaceAddress Address(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<CloudWorkspaceAddress>(value.GetRawText(), AddressOptions) ?? new CloudWorkspaceAddress()
            : new CloudWorkspaceAddress();
}
