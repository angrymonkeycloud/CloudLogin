using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Text;
using System.Text.Json;

namespace AngryMonkey.CloudLogin;

public partial class CloudLoginFullPageComponent
{
    //GENERAL VARIABLES--------------------------------------
    [Parameter] public string? Logo { get; set; }

    //VISUAL VARIABLES---------------------------------------

    protected string ButtonName { get; set; } = string.Empty;
    protected string Title { get; set; } = string.Empty;
    protected string Subtitle { get; set; } = string.Empty;
    protected string CssClass
    {
        get
        {
            List<string> classes = [];

            if (IsLoading)
                classes.Add("_loading");

            if (Next)
                classes.Add("_next");

            if (Preview)
                classes.Add("_preview");

            if (AnimateStep != AnimateBodyStep.None)
                classes.Add($"_animatestep-{AnimateStep.ToString().ToLower()}");

            if (AnimateDirection != AnimateBodyDirection.None)
                classes.Add($"_animatedirection-{AnimateDirection.ToString().ToLower()}");

            return string.Join(" ", classes);
        }
    }
    public bool IsLoading { get; set; } = false;
    protected bool Next { get; set; } = false;
    protected bool Preview { get; set; } = false;
    protected List<string> Errors { get; set; } = [];

    private AnimateBodyStep AnimateStep = AnimateBodyStep.None;

    private AnimateBodyDirection AnimateDirection = AnimateBodyDirection.None;

    //VERIFICATION VARIABLES---------------------------------
    public bool ExpiredCode { get; set; } = false;
    public string? VerificationCode { get; set; }
    public DateTimeOffset? VerificationCodeExpiry { get; set; }
}
